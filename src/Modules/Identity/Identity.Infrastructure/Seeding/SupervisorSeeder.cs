using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Seeding;

/// <summary>
/// Crée le premier Superviseur depuis la configuration. Sans effet dès qu'un Superviseur existe,
/// même bloqué : on ne recrée jamais un compte « de secours » qui contournerait les règles.
/// </summary>
internal sealed class SupervisorSeeder(
    IdentityModuleDbContext db,
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    IClock clock,
    IOptions<SupervisorSeedOptions> options,
    ILogger<SupervisorSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Accounts.AnyAsync(a => a.Role == RoleTemplate.Supervisor, cancellationToken))
        {
            return;
        }

        var seed = options.Value;

        // Sans Superviseur, personne ne peut créer de compte : on refuse de démarrer plutôt que
        // de lancer une app inutilisable.
        var email = Email.Create(seed.Email);
        if (email.IsFailure)
        {
            throw new InvalidOperationException(
                $"Aucun Superviseur en base et SeedSupervisor:Email invalide ({email.Error.Code}).");
        }

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(seed.Phone))
        {
            var parsedPhone = PhoneNumber.Create(seed.Phone);
            if (parsedPhone.IsFailure)
            {
                throw new InvalidOperationException(
                    $"SeedSupervisor:Phone invalide ({parsedPhone.Error.Code}).");
            }

            phone = parsedPhone.Value;
        }

        if (string.IsNullOrEmpty(seed.TemporaryPassword))
        {
            throw new InvalidOperationException(
                "Aucun Superviseur en base et SeedSupervisor:TemporaryPassword absent.");
        }

        var account = UserAccount.CreateWithTemporaryPassword(
            email.Value, phone, seed.FullName, RoleTemplate.Supervisor, clock.UtcNow);
        if (account.IsFailure)
        {
            throw new InvalidOperationException(
                $"SeedSupervisor invalide ({account.Error.Code}).");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Les identifiants d'abord : accounts.Id référence credentials.Id.
        var created = await credentials.CreateAsync(account.Value.Id, seed.TemporaryPassword, cancellationToken);
        if (created.IsFailure)
        {
            throw new InvalidOperationException(
                $"SeedSupervisor:TemporaryPassword refusé ({created.Error.Code}) : " +
                "8 caractères minimum, dont au moins un chiffre.");
        }

        accounts.Add(account.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Premier Superviseur créé (compte {AccountId}). Mot de passe à changer à la première connexion.",
            account.Value.Id);
    }
}