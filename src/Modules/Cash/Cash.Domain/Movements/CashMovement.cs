using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Cash.Domain.Movements;

/// <summary>Le type fixe le sens (règle validée) : l'agent ne choisit jamais + ou −.</summary>
public enum CashMovementType
{
    /// <summary>Encaissement : l'argent entre, venant d'un tiers (« Source »).</summary>
    Receipt,

    /// <summary>Décaissement : l'argent sort, vers un tiers.</summary>
    Payment,

    /// <summary>« Approvisionnement de caisse » : montant seul, contre « Apports du patron ».</summary>
    OwnerContribution,

    /// <summary>« Retrait du patron » : montant seul, contre « Apports du patron ».</summary>
    OwnerWithdrawal,
}

/// <summary>Ce que l'agent a saisi, déjà lu par l'Application (agence, devise, compte, tiers).</summary>
public sealed record NewCashMovement(
    Guid AgencyId,
    CashMovementType Type,
    Guid TreasuryAccountId,
    Guid? PartyId,
    Money Amount,
    string? Note,
    DateTimeOffset OperationAt);

/// <summary>
/// Un mouvement manuel de caisse (règles validées le 21 octobre 2026, écran « Caisse - Ajout » de l'app actuelle) :
/// méthode de paiement (compte de trésorerie), encaissement ou décaissement avec un tiers obligatoire,
/// ou approvisionnement / retrait du patron en montant seul ; montant dans la devise de l'agence ; note facultative ;
/// date d'opération (avec l'heure) jamais future. Montants MRO hors transferts : décimales permises.
/// </summary>
public sealed class CashMovement
{
    public const int NoteMaxLength = 500;
    public const int IdempotencyKeyMaxLength = 100;

    // Constructeur réservé à EF Core.
    private CashMovement()
    {
        IdempotencyKey = null!;
    }

    private CashMovement(NewCashMovement request, string idempotencyKey, Guid createdBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = request.AgencyId;
        Type = request.Type;
        TreasuryAccountId = request.TreasuryAccountId;
        PartyId = request.PartyId;
        Amount = request.Amount.Amount;
        Currency = request.Amount.Currency;
        Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        OperationAt = request.OperationAt;
        IdempotencyKey = idempotencyKey;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid AgencyId { get; private set; }

    public CashMovementType Type { get; private set; }

    public Guid TreasuryAccountId { get; private set; }

    /// <summary>Tiers de l'encaissement ou du décaissement ; null pour l'approvisionnement et le retrait du patron.</summary>
    public Guid? PartyId { get; private set; }

    /// <summary>Toujours positif ; le sens vient du type.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset OperationAt { get; private set; }

    public string IdempotencyKey { get; private set; }

    /// <summary>Écriture du grand livre du mouvement.</summary>
    public Guid EntryId { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Vrai si l'argent entre dans le compte de trésorerie (encaissement, approvisionnement).</summary>
    public bool IsIncoming => IsIncomingType(Type);

    public Money Money => Money.Create(Amount, Currency).Value;

    public static bool IsIncomingType(CashMovementType type) =>
        type is CashMovementType.Receipt or CashMovementType.OwnerContribution;

    public static bool NeedsParty(CashMovementType type) =>
        type is CashMovementType.Receipt or CashMovementType.Payment;

    public static Result<CashMovement> Create(
        NewCashMovement request, string? idempotencyKey, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > IdempotencyKeyMaxLength)
        {
            return CashErrors.Movement.IdempotencyKeyRequired;
        }

        if (!Enum.IsDefined(request.Type))
        {
            return CashErrors.Movement.TypeInvalid;
        }

        if (NeedsParty(request.Type) && request.PartyId is null)
        {
            return CashErrors.Movement.PartyRequired;
        }

        if (!NeedsParty(request.Type) && request.PartyId is not null)
        {
            return CashErrors.Movement.PartyNotAllowed;
        }

        if (request.Amount.Amount <= 0m)
        {
            return CashErrors.Movement.AmountInvalid;
        }

        if (request.OperationAt > now)
        {
            return CashErrors.Movement.FutureDate;
        }

        if ((request.Note?.Trim().Length ?? 0) > NoteMaxLength)
        {
            return CashErrors.Movement.NoteTooLong;
        }

        return new CashMovement(request, idempotencyKey.Trim(), createdBy, now);
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
            throw new InvalidOperationException("Ce mouvement a déjà son écriture.");
        }

        EntryId = entryId;
    }
}
