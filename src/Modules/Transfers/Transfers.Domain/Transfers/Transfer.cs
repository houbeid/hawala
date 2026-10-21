using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Transfers.Domain.Transfers;

/// <summary>Ce que l'agent a saisi, déjà lu par l'Application (taux du jour, agences, devises).</summary>
public sealed record NewTransfer(
    Guid SourceAgencyId,
    Currency SourceCurrency,
    Guid DestinationAgencyId,
    Currency DestinationCurrency,
    Money AmountXof,
    ExchangeRate SaleRate,
    ExchangeRate PurchaseRate,
    string? SenderInfo,
    string? BeneficiaryInfo,
    string? Note,
    DateTimeOffset OperationAt,
    IReadOnlyList<PaymentLine> Payments,
    Guid? CreditPartyId = null);

/// <summary>
/// Un transfert entre Nouakchott (MRO) et Dakar (FCFA). Règles validées :
/// - créé par l'agence d'envoi ; montant de référence en FCFA (entier) ; MRO = FCFA × prix de vente / 5000,
///   arrondi UNE fois à la création (AwayFromZero), enregistré et jamais recalculé ;
/// - le client paie dans la devise de l'agence d'envoi, le bénéficiaire reçoit celle de l'agence de destination ;
/// - chaque transfert garde ses deux taux (prix de vente appliqué, prix d'achat du jour pour le bénéfice) ;
/// - expéditeur et bénéficiaire en texte libre ; date de l'opération (avec l'heure) jamais dans le futur ;
/// - répartition des paiements : total = montant payé, sauf crédit activé ;
/// - crédit activé (validé le 20 octobre 2026) : la part non payée (montant − total réparti, partielle ou totale)
///   va en dette sur un tiers choisi ; un crédit sans part à crédit est refusé ;
/// - code de retrait : 6 chiffres, seul son hash est gardé ; statut de départ « non livré ».
/// </summary>
public sealed class Transfer
{
    public const int PartyInfoMaxLength = 200;
    public const int NoteMaxLength = 500;
    public const int ReferenceMaxLength = 60;
    public const int IdempotencyKeyMaxLength = 100;

    /// <summary>Au 5e code faux, le transfert est bloqué (règle validée) ; déblocage par le Superviseur.</summary>
    public const int MaxCodeAttempts = 5;

    private readonly List<TransferPayment> _payments = [];

    // Constructeur réservé à EF Core.
    private Transfer()
    {
        Reference = null!;
        SaleRate = null!;
        PurchaseRate = null!;
        SenderInfo = null!;
        BeneficiaryInfo = null!;
        WithdrawalCodeHash = null!;
        IdempotencyKey = null!;
    }

    private Transfer(NewTransfer request, decimal amountMro, decimal creditAmount, string reference, string codeHash, string idempotencyKey, Guid createdBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Reference = reference;
        SourceAgencyId = request.SourceAgencyId;
        SourceCurrency = request.SourceCurrency;
        DestinationAgencyId = request.DestinationAgencyId;
        DestinationCurrency = request.DestinationCurrency;
        AmountXof = request.AmountXof.Amount;
        AmountMro = amountMro;
        SaleRate = request.SaleRate;
        PurchaseRate = request.PurchaseRate;
        SenderInfo = request.SenderInfo!.Trim();
        BeneficiaryInfo = request.BeneficiaryInfo!.Trim();
        Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        OperationAt = request.OperationAt;
        CreditPartyId = request.CreditPartyId;
        CreditAmount = creditAmount;
        Status = TransferStatus.NotDelivered;
        WithdrawalCodeHash = codeHash;
        IdempotencyKey = idempotencyKey;
        CreatedBy = createdBy;
        CreatedAt = now;
        _payments.AddRange(request.Payments.Select(p => new TransferPayment(PaymentStage.Creation, p.TreasuryAccountId, p.Amount, now)));
    }

    public Guid Id { get; private set; }

    /// <summary>Référence lisible (format de l'app actuelle, sens des codes à préciser).</summary>
    public string Reference { get; private set; }

    public Guid SourceAgencyId { get; private set; }

    public Currency SourceCurrency { get; private set; }

    public Guid DestinationAgencyId { get; private set; }

    public Currency DestinationCurrency { get; private set; }

    /// <summary>Montant de référence, en FCFA entier.</summary>
    public decimal AmountXof { get; private set; }

    /// <summary>FCFA × prix de vente / 5000, arrondi une fois à la création ; jamais recalculé.</summary>
    public decimal AmountMro { get; private set; }

    /// <summary>Prix de vente appliqué au client.</summary>
    public ExchangeRate SaleRate { get; private set; }

    /// <summary>Prix d'achat du jour (taux du marché) : sert au bénéfice ; réservé au Superviseur à l'affichage.</summary>
    public ExchangeRate PurchaseRate { get; private set; }

    public string SenderInfo { get; private set; }

    public string BeneficiaryInfo { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Date et heure de l'opération saisies par l'agent (passées permises, futures refusées).</summary>
    public DateTimeOffset OperationAt { get; private set; }

    /// <summary>Tiers qui porte la dette quand le crédit est activé ; null sinon.</summary>
    public Guid? CreditPartyId { get; private set; }

    /// <summary>Part à crédit, dans la devise de l'agence d'envoi (0 sans crédit).</summary>
    public decimal CreditAmount { get; private set; }

    public TransferStatus Status { get; private set; }

    /// <summary>Hash du code de retrait : le code lui-même n'est jamais gardé, ni lisible par un agent.</summary>
    public string WithdrawalCodeHash { get; private set; }

    /// <summary>Essais faux du code de retrait (5 = transfert bloqué, déblocage par le Superviseur).</summary>
    public int FailedCodeAttempts { get; private set; }

    /// <summary>Clé envoyée par l'appareil : un double clic ou une resynchronisation ne crée pas deux transferts.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Écriture du grand livre passée à la création.</summary>
    public Guid CreationEntryId { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date et heure de la livraison saisies (maintenant par défaut) ; null tant que non livré.</summary>
    public DateTimeOffset? DeliveryOperationAt { get; private set; }

    /// <summary>Instant d'enregistrement de la livraison.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    public Guid? DeliveredBy { get; private set; }

    public string? DeliveryNote { get; private set; }

    /// <summary>Écriture du grand livre passée à la livraison.</summary>
    public Guid? DeliveryEntryId { get; private set; }

    /// <summary>Date et heure de l'annulation saisies (maintenant par défaut) ; null tant que non annulé.</summary>
    public DateTimeOffset? CancellationOperationAt { get; private set; }

    /// <summary>Instant d'enregistrement de l'annulation.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    /// <summary>Motif obligatoire de l'annulation (règle validée).</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Contre-passation de l'écriture de création, passée à l'annulation.</summary>
    public Guid? CancellationEntryId { get; private set; }

    /// <summary>Bloqué après 5 codes faux : plus personne ne livre jusqu'au déblocage par le Superviseur.</summary>
    public bool IsCodeLocked => FailedCodeAttempts >= MaxCodeAttempts;

    /// <summary>Essais de code restants avant blocage.</summary>
    public int RemainingCodeAttempts => Math.Max(0, MaxCodeAttempts - FailedCodeAttempts);

    public IReadOnlyList<TransferPayment> Payments => _payments;

    /// <summary>
    /// Bénéfice du transfert en MRO, en pleine précision (règle d'arrondi 4 : seul le total affiché est arrondi).
    /// Formules validées, T = prix d'achat du jour, F = FCFA, M = MRO enregistré :
    /// on reçoit des MRO et on paie des FCFA (envoi depuis l'agence en MRO) : B = M − F × T / 5000 ;
    /// on reçoit des FCFA et on paie des MRO (envoi depuis l'agence en FCFA) : B = F × T / 5000 − M.
    /// </summary>
    public decimal ProfitMro
    {
        get
        {
            var atPurchaseRate = AmountXof * PurchaseRate.MroPer5000Xof / ExchangeRate.XofUnit;
            return SourceCurrency == Currency.MRO ? AmountMro - atPurchaseRate : atPurchaseRate - AmountMro;
        }
    }

    /// <summary>Part à crédit en <see cref="Money"/>, ou null sans crédit.</summary>
    public Money? CreditMoney => CreditPartyId is null ? null : Money.Create(CreditAmount, SourceCurrency).Value;

    /// <summary>Montant du transfert dans une devise : MRO enregistré (arrondi à la création) ou FCFA saisi.</summary>
    public decimal AmountIn(Currency currency) => currency == Currency.MRO ? AmountMro : AmountXof;

    /// <summary>Ce que le client paie, dans la devise de l'agence d'envoi.</summary>
    public Money PaidAmount => Money.Create(SourceCurrency == Currency.MRO ? AmountMro : AmountXof, SourceCurrency).Value;

    /// <summary>Ce que le bénéficiaire reçoit, dans la devise de l'agence de destination.</summary>
    public Money AmountToDeliver => Money.Create(DestinationCurrency == Currency.MRO ? AmountMro : AmountXof, DestinationCurrency).Value;

    public static Result<Transfer> Create(
        NewTransfer request,
        string reference,
        string withdrawalCodeHash,
        string? idempotencyKey,
        Guid createdBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawalCodeHash);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > IdempotencyKeyMaxLength)
        {
            return TransfersErrors.Transfer.IdempotencyKeyRequired;
        }

        // Un seul corridor : une agence en MRO vers une agence en FCFA, ou l'inverse.
        if (request.SourceAgencyId == request.DestinationAgencyId
            || request.SourceCurrency == request.DestinationCurrency)
        {
            return TransfersErrors.Transfer.CorridorInvalid;
        }

        if (request.AmountXof.Currency != Currency.XOF || request.AmountXof.Amount <= 0m)
        {
            return TransfersErrors.Transfer.AmountInvalid;
        }

        if (request.OperationAt > now)
        {
            return TransfersErrors.Transfer.FutureDate;
        }

        var texts = ValidateTexts(request);
        if (texts.IsFailure)
        {
            return texts.Error;
        }

        // Arrondi une seule fois, ici (règles d'arrondi 2 et 3).
        var amountMro = request.SaleRate.ToTransferMro(request.AmountXof).Amount;
        var paid = request.SourceCurrency == Currency.MRO ? amountMro : request.AmountXof.Amount;

        var credit = ValidatePayments(request.Payments, request.SourceCurrency, paid, request.CreditPartyId is not null);
        if (credit.IsFailure)
        {
            return credit.Error;
        }

        return new Transfer(request, amountMro, credit.Value, reference, withdrawalCodeHash, idempotencyKey.Trim(), createdBy, now);
    }

    /// <summary>Peut-on encore livrer ce transfert ? Non livré et pas bloqué par les codes faux.</summary>
    public Result EnsureDeliverable() => Status switch
    {
        TransferStatus.Delivered => TransfersErrors.Delivery.AlreadyDelivered,
        TransferStatus.Cancelled => TransfersErrors.Delivery.Cancelled,
        _ when IsCodeLocked => TransfersErrors.Delivery.CodeLocked,
        _ => Result.Success(),
    };

    /// <summary>
    /// Compte un code faux (par transfert, pas par agent) et renvoie l'erreur à montrer :
    /// code faux (voir <see cref="RemainingCodeAttempts"/>), ou blocage au 5e.
    /// </summary>
    public Error RegisterWrongCode()
    {
        if (IsCodeLocked)
        {
            return TransfersErrors.Delivery.CodeLocked;
        }

        FailedCodeAttempts++;
        return IsCodeLocked
            ? TransfersErrors.Delivery.CodeLocked
            : TransfersErrors.Delivery.WrongCode;
    }

    /// <summary>
    /// Vérifie une livraison sans rien changer (pour ne pas consommer d'essai de code sur une erreur de saisie) :
    /// livrable ; date pas dans le futur ni avant le transfert ; répartition dans la devise de destination,
    /// montants positifs, méthodes distinctes, total = montant à livrer.
    /// </summary>
    public Result ValidateDelivery(IReadOnlyList<PaymentLine> lines, DateTimeOffset operationAt, string? note, DateTimeOffset now)
    {
        var deliverable = EnsureDeliverable();
        if (deliverable.IsFailure)
        {
            return deliverable;
        }

        if (operationAt > now)
        {
            return TransfersErrors.Transfer.FutureDate;
        }

        if (operationAt < OperationAt)
        {
            return TransfersErrors.Delivery.DateBeforeTransfer;
        }

        if ((note?.Trim().Length ?? 0) > NoteMaxLength)
        {
            return TransfersErrors.Delivery.NoteTooLong;
        }

        if (lines is null || lines.Count == 0)
        {
            return TransfersErrors.Payment.Required;
        }

        if (lines.Select(l => l.TreasuryAccountId).Distinct().Count() != lines.Count)
        {
            return TransfersErrors.Payment.AccountDuplicated;
        }

        foreach (var line in lines)
        {
            if (line.Amount.Currency != DestinationCurrency)
            {
                return TransfersErrors.Payment.CurrencyMismatch;
            }

            if (line.Amount.Amount <= 0m)
            {
                return TransfersErrors.Payment.AmountNotPositive;
            }
        }

        return lines.Sum(l => l.Amount.Amount) == AmountToDeliver.Amount
            ? Result.Success()
            : TransfersErrors.Delivery.TotalMismatch;
    }

    /// <summary>Livre le transfert (une seule livraison, éventuellement répartie) : il devient « livré » et verrouillé.</summary>
    public Result Deliver(
        IReadOnlyList<PaymentLine> lines, DateTimeOffset operationAt, string? note, Guid deliveredBy, DateTimeOffset now)
    {
        var valid = ValidateDelivery(lines, operationAt, note, now);
        if (valid.IsFailure)
        {
            return valid;
        }

        _payments.AddRange(lines.Select(l => new TransferPayment(PaymentStage.Delivery, l.TreasuryAccountId, l.Amount, now)));
        Status = TransferStatus.Delivered;
        DeliveryOperationAt = operationAt;
        DeliveredAt = now;
        DeliveredBy = deliveredBy;
        DeliveryNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Annule un transfert non livré (Superviseur seulement, règle validée) : motif obligatoire (500 caractères au plus),
    /// date jamais future ni avant le transfert. Un transfert livré ne s'annule pas (pour l'instant) ;
    /// un transfert bloqué par les codes faux peut l'être. Annulé = verrouillé.
    /// </summary>
    public Result Cancel(string? reason, DateTimeOffset operationAt, Guid cancelledBy, DateTimeOffset now)
    {
        if (Status == TransferStatus.Cancelled)
        {
            return TransfersErrors.Cancellation.AlreadyCancelled;
        }

        if (Status == TransferStatus.Delivered)
        {
            return TransfersErrors.Cancellation.Delivered;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return TransfersErrors.Cancellation.ReasonRequired;
        }

        if (reason.Trim().Length > NoteMaxLength)
        {
            return TransfersErrors.Cancellation.ReasonTooLong;
        }

        if (operationAt > now)
        {
            return TransfersErrors.Transfer.FutureDate;
        }

        if (operationAt < OperationAt)
        {
            return TransfersErrors.Cancellation.DateBeforeTransfer;
        }

        Status = TransferStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancellationOperationAt = operationAt;
        CancelledAt = now;
        CancelledBy = cancelledBy;
        return Result.Success();
    }

    /// <summary>Rattache la contre-passation de l'écriture de création, une seule fois et seulement après l'annulation.</summary>
    public void AttachCancellationEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (Status != TransferStatus.Cancelled || CancellationEntryId is not null)
        {
            throw new InvalidOperationException("Contre-passation sans annulation, ou déjà rattachée.");
        }

        CancellationEntryId = entryId;
    }

    /// <summary>Rattache l'écriture de livraison, une seule fois.</summary>
    public void AttachDeliveryEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (Status != TransferStatus.Delivered || DeliveryEntryId is not null)
        {
            throw new InvalidOperationException("Écriture de livraison sans livraison, ou déjà rattachée.");
        }

        DeliveryEntryId = entryId;
    }

    /// <summary>Rattache l'écriture de création, une seule fois : un second appel est un bug de programmation.</summary>
    public void AttachCreationEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (CreationEntryId != Guid.Empty)
        {
            throw new InvalidOperationException("Ce transfert a déjà son écriture de création.");
        }

        CreationEntryId = entryId;
    }

    private static Result ValidateTexts(NewTransfer request)
    {
        if (string.IsNullOrWhiteSpace(request.SenderInfo))
        {
            return TransfersErrors.Transfer.SenderRequired;
        }

        if (string.IsNullOrWhiteSpace(request.BeneficiaryInfo))
        {
            return TransfersErrors.Transfer.BeneficiaryRequired;
        }

        if (request.SenderInfo.Trim().Length > PartyInfoMaxLength
            || request.BeneficiaryInfo.Trim().Length > PartyInfoMaxLength
            || (request.Note?.Trim().Length ?? 0) > NoteMaxLength)
        {
            return TransfersErrors.Transfer.TextTooLong;
        }

        return Result.Success();
    }

    /// <summary>Valide la répartition et renvoie la part à crédit (0 sans crédit).</summary>
    private static Result<decimal> ValidatePayments(
        IReadOnlyList<PaymentLine> payments, Currency currency, decimal expectedTotal, bool creditEnabled)
    {
        payments ??= [];

        // Crédit total (validé) : avec le crédit activé, la répartition peut être vide.
        if (payments.Count == 0 && !creditEnabled)
        {
            return TransfersErrors.Payment.Required;
        }

        if (payments.Select(p => p.TreasuryAccountId).Distinct().Count() != payments.Count)
        {
            return TransfersErrors.Payment.AccountDuplicated;
        }

        foreach (var payment in payments)
        {
            if (payment.Amount.Currency != currency)
            {
                return TransfersErrors.Payment.CurrencyMismatch;
            }

            if (payment.Amount.Amount <= 0m)
            {
                return TransfersErrors.Payment.AmountNotPositive;
            }
        }

        var total = payments.Sum(p => p.Amount.Amount);
        if (!creditEnabled)
        {
            return total == expectedTotal ? 0m : TransfersErrors.Payment.TotalMismatch;
        }

        if (total > expectedTotal)
        {
            return TransfersErrors.Payment.TotalMismatch;
        }

        // Crédit activé mais tout payé : refusé (validé), pour ne pas rattacher un tiers sans dette.
        return total == expectedTotal ? TransfersErrors.Credit.NotNeeded : expectedTotal - total;
    }
}
