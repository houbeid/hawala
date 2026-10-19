using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.DeactivateTreasuryAccount;

/// <summary>
/// Lit le solde au grand livre, désactive le compte de trésorerie (refusé si le solde n'est pas nul),
/// puis ferme son compte au grand livre : plus aucune écriture ne peut y arriver. Une seule transaction commune.
/// POINT OUVERT : une écriture enregistrée entre la lecture du solde et la fermeture n'est pas bloquée (pas de verrou).
/// Accepté : la désactivation est rare et faite par le Superviseur ; un verrou sur le compte du grand livre
/// sera ajouté si les transferts (phase 3) rendent ce cas réaliste.
/// POINT OUVERT : l'audit « qui a désactivé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class DeactivateTreasuryAccountCommandHandler(
    ITreasuryAccountRepository accounts,
    ILedger ledger,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivateTreasuryAccountCommand>
{
    public async Task<Result> Handle(DeactivateTreasuryAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
        }

        var balances = await ledger.GetBalancesAsync([account.LedgerAccountId], asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var deactivated = account.Deactivate(balances.Value.Single().Balance);
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var closed = await ledger.CloseAccountAsync(account.LedgerAccountId, cancellationToken);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
