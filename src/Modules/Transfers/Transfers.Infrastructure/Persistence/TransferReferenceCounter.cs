namespace Transfers.Infrastructure.Persistence;

/// <summary>Dernier numéro de référence donné, par agence d'envoi et par jour (table technique).</summary>
internal sealed class TransferReferenceCounter
{
    public Guid AgencyId { get; private set; }

    public DateOnly Day { get; private set; }

    public int LastValue { get; private set; }
}
