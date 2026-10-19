using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Ledger.Contracts;

namespace Ledger.Domain.Entries;

/// <summary>
/// Une ligne d'écriture : un compte, un sens (débit ou crédit), un montant strictement positif.
/// Immuable : aucune méthode ne la modifie, et la base refuse tout UPDATE / DELETE.
/// </summary>
public sealed class JournalLine
{
    // Constructeur réservé à EF Core.
    private JournalLine()
    {
    }

    internal JournalLine(Guid id, Guid accountId, EntrySide side, decimal amount, Currency currency)
    {
        Id = id;
        AccountId = accountId;
        Side = side;
        Amount = amount;
        Currency = currency;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public EntrySide Side { get; private set; }

    /// <summary>Toujours strictement positif : c'est <see cref="Side"/> qui dit débit ou crédit.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Contribution au solde du compte (débit − crédit) : + pour un débit, − pour un crédit.</summary>
    public decimal SignedAmount => Side == EntrySide.Debit ? Amount : -Amount;

    /// <summary>Même compte et même montant, sens inversé : sert à la contre-passation.</summary>
    internal JournalLine Inverted(DateTimeOffset now) =>
        new(IdGenerator.NewId(now), AccountId, Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit, Amount, Currency);
}
