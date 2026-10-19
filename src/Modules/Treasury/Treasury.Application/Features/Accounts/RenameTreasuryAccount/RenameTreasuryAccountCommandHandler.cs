using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Treasury.Application.Abstractions;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.RenameTreasuryAccount;

/// <summary>
/// POINT OUVERT : le compte au grand livre garde le nom donné à la création. Les écrans et relevés affichent
/// le nom tenu par la Trésorerie ; ajouter un « renommer » au grand livre seulement si un écran en a besoin.
/// POINT OUVERT : l'audit « qui a renommé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class RenameTreasuryAccountCommandHandler(
    ITreasuryAccountRepository accounts,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RenameTreasuryAccountCommand>
{
    public async Task<Result> Handle(RenameTreasuryAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
        }

        // Unicité vérifiée avant de modifier le compte : un refus laisse l'entité suivie intacte.
        var newName = command.Name?.Trim() ?? string.Empty;
        if (newName.Length > 0
            && await accounts.NameExistsAsync(account.AgencyId, newName, account.Id, cancellationToken))
        {
            return TreasuryErrors.Account.NameTaken;
        }

        var renamed = account.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
