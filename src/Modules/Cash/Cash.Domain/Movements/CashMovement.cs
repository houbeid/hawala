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

    /// <summary>Virement interne : d'un compte de trésorerie à un autre de la même agence (ni entrée ni sortie pour l'agence).</summary>
    InternalTransfer,
}

/// <summary>Un mouvement annulé (Superviseur, motif obligatoire) est contre-passé et ne compte plus dans les totaux.</summary>
public enum CashMovementStatus
{
    Active,
    Cancelled,
}

/// <summary>Ce que l'agent a saisi, déjà lu par l'Application (agence, devise, compte, tiers).</summary>
public sealed record NewCashMovement(
    Guid AgencyId,
    CashMovementType Type,
    Guid TreasuryAccountId,
    Guid? PartyId,
    Money Amount,
    string? Note,
    DateTimeOffset OperationAt,
    Guid? DestinationTreasuryAccountId = null);

/// <summary>
/// Un mouvement manuel de caisse (règles validées le 21 octobre 2026, écran « Caisse - Ajout » de l'app actuelle) :
/// méthode de paiement (compte de trésorerie), encaissement ou décaissement avec un tiers obligatoire,
/// ou approvisionnement / retrait du patron en montant seul ; montant dans la devise de l'agence ; note facultative ;
/// date d'opération (avec l'heure) jamais future. Montants MRO hors transferts : décimales permises.
/// Virement interne (règle validée : deux lignes) : d'un compte de trésorerie vers un autre de la même agence, sans tiers.
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
        DestinationTreasuryAccountId = request.DestinationTreasuryAccountId;
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

    /// <summary>Tiers de l'encaissement ou du décaissement ; null pour les autres types.</summary>
    public Guid? PartyId { get; private set; }

    /// <summary>Compte qui reçoit un virement interne ; null pour les autres types (le compte d'origine est TreasuryAccountId).</summary>
    public Guid? DestinationTreasuryAccountId { get; private set; }

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

    public CashMovementStatus Status { get; private set; }

    /// <summary>Motif obligatoire de l'annulation ; null tant que le mouvement est actif.</summary>
    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    /// <summary>Contre-passation de l'écriture du mouvement.</summary>
    public Guid? CancellationEntryId { get; private set; }

    /// <summary>Vrai si l'argent entre dans l'agence (encaissement, approvisionnement).</summary>
    public bool IsIncoming => IsIncomingType(Type);

    /// <summary>Vrai pour un virement interne : ni entrée ni sortie pour l'agence.</summary>
    public bool IsInternal => Type == CashMovementType.InternalTransfer;

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

        if (request.Type == CashMovementType.InternalTransfer)
        {
            if (request.DestinationTreasuryAccountId is not { } destination)
            {
                return CashErrors.Movement.DestinationRequired;
            }

            if (destination == request.TreasuryAccountId)
            {
                return CashErrors.Movement.SameAccount;
            }
        }
        else if (request.DestinationTreasuryAccountId is not null)
        {
            return CashErrors.Movement.DestinationNotAllowed;
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

    /// <summary>
    /// Annule le mouvement (Superviseur seulement, règle validée) : motif obligatoire ; l'Application contre-passe son écriture
    /// au jour de l'annulation. Annulé = verrouillé.
    /// </summary>
    public Result Cancel(string? reason, Guid cancelledBy, DateTimeOffset now)
    {
        if (Status == CashMovementStatus.Cancelled)
        {
            return CashErrors.Movement.AlreadyCancelled;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return CashErrors.Movement.ReasonRequired;
        }

        if (reason.Trim().Length > NoteMaxLength)
        {
            return CashErrors.Movement.ReasonTooLong;
        }

        Status = CashMovementStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = now;
        CancelledBy = cancelledBy;
        return Result.Success();
    }

    /// <summary>Rattache la contre-passation, une seule fois et seulement après l'annulation.</summary>
    public void AttachCancellationEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (Status != CashMovementStatus.Cancelled || CancellationEntryId is not null)
        {
            throw new InvalidOperationException("Contre-passation sans annulation, ou déjà rattachée.");
        }

        CancellationEntryId = entryId;
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
