using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.ReactivateTreasuryAccount;

/// <summary>
/// Réactive le compte de trésorerie et rouvre son compte au grand livre, dans une seule transaction commune.
/// POINT OUVERT : l'audit « qui a réactivé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class ReactivateTreasuryAccountCommandHandler(
    ITreasuryAccountRepository accounts,
    ILedger ledger,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ReactivateTreasuryAccountCommand>
{
    public async Task<Result> Handle(ReactivateTreasuryAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
        }

        var reactivated = account.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        var reopened = await ledger.ReopenAccountAsync(account.LedgerAccountId, cancellationToken);
        if (reopened.IsFailure)
        {
            return reopened.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
