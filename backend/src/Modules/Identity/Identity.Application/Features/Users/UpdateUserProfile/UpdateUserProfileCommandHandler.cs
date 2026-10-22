using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;

namespace Identity.Application.Features.Users.UpdateUserProfile;

/// <summary>
/// Les sessions ne sont pas coupées (règle validée) : l'agent garde sa session, seul l'identifiant
/// de sa prochaine connexion change.
/// Transaction commune : le profil et sa ligne d'audit ensemble.
/// Tracé dans l'audit (« Identity.Account.ProfileUpdated », avant / après) s'il a changé quelque chose.
/// </summary>
public sealed class UpdateUserProfileCommandHandler(
    IUserAccountRepository accounts,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<UpdateUserProfileCommand>
{
    public async Task<Result> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
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

        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        // Hors de son périmètre, le compte n'existe pas pour lui (même réponse qu'un id inconnu).
        if (account is null || !manager.CanManage(account))
        {
            return IdentityErrors.Account.NotFound;
        }

        // Unicité vérifiée avant de modifier le compte : un refus laisse l'entité suivie intacte.
        // L'index unique en base reste le vrai garde-fou en cas de course.
        if (await accounts.EmailExistsAsync(email.Value, account.Id, cancellationToken))
        {
            return IdentityErrors.Account.EmailTaken;
        }

        if (phone is not null && await accounts.PhoneExistsAsync(phone, account.Id, cancellationToken))
        {
            return IdentityErrors.Account.PhoneTaken;
        }

        var before = (Name: account.FullName, Email: account.Email.Value, Phone: account.Phone?.Value);
        var updated = account.UpdateProfile(email.Value, phone, command.FullName);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var changes = new[]
        {
            AccountAudit.Change("fullName", before.Name, account.FullName),
            AccountAudit.Change("email", before.Email, account.Email.Value),
            AccountAudit.Change("phone", before.Phone, account.Phone?.Value),
        };
        if (changes.All(c => c.Before == c.After))
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await AccountAudit.RecordAsync(
            audit, AccountAudit.ProfileUpdated, account, command.ActorId, changes, cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }
}
