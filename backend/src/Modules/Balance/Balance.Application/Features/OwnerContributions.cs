using Ledger.Contracts;

namespace Balance.Application.Features;

/// <summary>Lecture des apports du patron au grand livre, partagée par le bilan par caisse et le bilan général.</summary>
internal static class OwnerContributions
{
    /// <summary>
    /// Approvisionnements − retraits du patron dans une caisse : « Apports du patron » est crédité à chaque approvisionnement,
    /// son solde (débit − crédit) est donc négatif quand le patron a apporté plus qu'il n'a retiré.
    /// </summary>
    public static async Task<decimal> ReadAsync(ILedger ledger, Guid agencyId, CancellationToken cancellationToken)
    {
        var account = await ledger.FindSystemAccountAsync(SystemAccountRole.OwnerContributions, agencyId, cancellationToken);
        if (account is not { } id)
        {
            return 0m;
        }

        var balances = await ledger.GetBalancesAsync([id], asOf: null, cancellationToken);
        return balances.IsSuccess ? -balances.Value.Sum(b => b.Balance.Amount) : 0m;
    }
}
