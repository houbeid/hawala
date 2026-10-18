using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Organization.Contracts;

namespace Identity.Application.Features.Users.CreateUser;

/// <summary>
/// Crée le compte et ses identifiants dans une seule transaction (voir l'enregistrement en Infrastructure).
/// POINT OUVERT : l'audit « qui a créé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class CreateUserCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    ITemporaryPasswordGenerator passwords,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork,
    IClock clock)
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

        if (!EnumNameParser.TryParse<RoleTemplate>(command.Role, out var role))
        {
            return IdentityErrors.Account.RoleInvalid;
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

        return new CreatedUser(account.Value.Id, temporaryPassword);
    }
}
