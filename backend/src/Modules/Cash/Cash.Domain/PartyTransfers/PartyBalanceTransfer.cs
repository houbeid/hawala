using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Cash.Domain.PartyTransfers;

/// <summary>
/// Transfert de solde entre deux tiers d'une même agence, sans toucher aux caisses (décisions du patron, 2 novembre 2026) :
/// le tiers débité « donne » le montant au tiers crédité. Exemple : on doit 1 000 000 à A ; A demande d'en donner 400 000
/// à B ; on doit désormais 600 000 à A et 400 000 de plus à B. Superviseur et Agent, sans motif (note facultative),
/// dans la devise de l'agence ; le plafond de crédit du tiers débité est bloquant (contrôlé par le handler).
/// </summary>
public sealed class PartyBalanceTransfer
{
    public const int NoteMaxLength = 500;
    public const int IdempotencyKeyMaxLength = 100;

    // Constructeur réservé à EF Core.
    private PartyBalanceTransfer()
    {
        IdempotencyKey = null!;
    }

    private PartyBalanceTransfer(
        Guid agencyId, Guid fromPartyId, Guid toPartyId, Money money, string? note, DateTimeOffset operationAt,
        string idempotencyKey, Guid createdBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        FromPartyId = fromPartyId;
        ToPartyId = toPartyId;
        Amount = money.Amount;
        Currency = money.Currency;
        Note = note;
        OperationAt = operationAt;
        IdempotencyKey = idempotencyKey;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid AgencyId { get; private set; }

    /// <summary>Tiers débité : il nous doit davantage (ou on lui doit moins).</summary>
    public Guid FromPartyId { get; private set; }

    /// <summary>Tiers crédité : il nous doit moins (ou on lui doit davantage).</summary>
    public Guid ToPartyId { get; private set; }

    /// <summary>Strictement positif, dans la devise de l'agence.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset OperationAt { get; private set; }

    /// <summary>Clé envoyée par l'appareil : un double clic n'enregistre pas deux transferts de solde.</summary>
    public string IdempotencyKey { get; private set; }

    public Guid EntryId { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Money => Money.Create(Amount, Currency).Value;

    /// <param name="money">Montant déjà lu dans la devise de l'agence (MRU converti, précision vérifiée).</param>
    public static Result<PartyBalanceTransfer> Create(
        Guid agencyId,
        Guid? fromPartyId,
        Guid? toPartyId,
        Money money,
        string? note,
        DateTimeOffset operationAt,
        string idempotencyKey,
        Guid createdBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(money);

        if (fromPartyId is not { } from || toPartyId is not { } to || from == Guid.Empty || to == Guid.Empty)
        {
            return CashErrors.PartyTransfer.PartyRequired;
        }

        if (from == to)
        {
            return CashErrors.PartyTransfer.SameParty;
        }

        if (money.Amount <= 0m)
        {
            return CashErrors.Movement.AmountInvalid;
        }

        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is { Length: > NoteMaxLength })
        {
            return CashErrors.Movement.NoteTooLong;
        }

        if (operationAt > now)
        {
            return CashErrors.Movement.FutureDate;
        }

        return new PartyBalanceTransfer(agencyId, from, to, money, text, operationAt, idempotencyKey, createdBy, now);
    }

    /// <summary>Rattache l'écriture du grand livre, une seule fois.</summary>
    public void AttachEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (EntryId != Guid.Empty)
        {
            throw new InvalidOperationException("Ce transfert de solde a déjà son écriture.");
        }

        EntryId = entryId;
    }
}
