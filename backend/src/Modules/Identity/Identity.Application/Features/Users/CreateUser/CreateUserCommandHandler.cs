using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Organization.Contracts;

namespace Identity.Application.Features.Users.CreateUser;

/// <summary>
/// Crée le compte et ses identifiants dans une seule transaction (voir l'enregistrement en Infrastructure).
/// Tracé dans l'audit (« Identity.Account.Created ») dans la même transaction ; jamais le mot de passe temporaire.
/// </summary>
public sealed class CreateUserCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    ITemporaryPasswordGenerator passwords,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<CreateUserCommand, CreatedUser>
{
    public async Task<Result<CreatedUser>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
        {
            return email.Error;
        }

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(command.Phone))
        {
            var parsedPhone = PhoneNumber.Create(command.Phone);
            if (parsedPhone.IsFailure)
            {
                return parsedPhone.Error;
            }

            phone = parsedPhone.Value;
        }

        if (!EnumNames.TryParse<RoleTemplate>(command.Role, out var role))
        {
            return IdentityErrors.Account.RoleInvalid;
        }

        // Règle validée le 21 octobre 2026 : hors Superviseur, un compte de son agence, jamais un Superviseur.
        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        if (!manager.IsSupervisor)
        {
            if (role == RoleTemplate.Supervisor)
            {
                return IdentityErrors.Account.SupervisorOnly;
            }

            if (command.AgencyId is { } requested && requested != Guid.Empty && requested != manager.AgencyId)
            {
                return IdentityErrors.Account.OtherAgency;
            }
        }

        // L'agence doit exister dans le module Organisation (la règle agent / Superviseur est dans le Domain).
        if (command.AgencyId is { } agencyId && agencyId != Guid.Empty
            && await agencies.GetByIdAsync(agencyId, cancellationToken) is null)
        {
            return IdentityErrors.Account.AgencyNotFound;
        }

        var account = UserAccount.CreateWithTemporaryPassword(
            email.Value, phone, command.FullName, role, command.AgencyId, clock.UtcNow);
        if (account.IsFailure)
        {
            return account.Error;
        }

        // Vérifié avant d'écrire pour un message clair ; l'index unique en base reste le vrai garde-fou
        // si deux Superviseurs créent le même email à la même seconde.
        if (await accounts.EmailExistsAsync(email.Value, cancellationToken))
        {
            return IdentityErrors.Account.EmailTaken;
        }

        if (phone is not null && await accounts.PhoneExistsAsync(phone, cancellationToken))
        {
            return IdentityErrors.Account.PhoneTaken;
        }

        var temporaryPassword = passwords.Generate();

        // Les identifiants d'abord : accounts.Id référence credentials.Id.
        var created = await credentials.CreateAsync(account.Value.Id, temporaryPassword, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        accounts.Add(account.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var newAccount = account.Value;
        var audited = await AccountAudit.RecordAsync(
            audit,
            AccountAudit.Created,
            newAccount,
            command.ActorId,
            [
                AccountAudit.Change("fullName", null, newAccount.FullName),
                AccountAudit.Change("email", null, newAccount.Email.Value),
                AccountAudit.Change("phone", null, newAccount.Phone?.Value),
                AccountAudit.Change("role", null, newAccount.Role),
                AccountAudit.Change("agencyId", null, newAccount.AgencyId),
            ],
            cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return new CreatedUser(newAccount.Id, temporaryPassword);
    }
}
