using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;

namespace Identity.Application.Features.Users.UpdateUserProfile;

/// <summary>
/// Les sessions ne sont pas coupées (règle validée) : l'agent garde sa session, seul l'identifiant
/// de sa prochaine connexion change.
/// Pas de transaction explicite : une seule table, un seul enregistrement.
/// POINT OUVERT : l'audit « qui a modifié ce profil » (avant / après) arrivera avec le module Audit.
/// </summary>
public sealed class UpdateUserProfileCommandHandler(
    IUserAccountRepository accounts,
    IUnitOfWork unitOfWork)
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

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
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

        var updated = account.UpdateProfile(email.Value, phone, command.FullName);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
