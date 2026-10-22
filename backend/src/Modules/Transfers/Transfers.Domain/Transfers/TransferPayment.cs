using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;

namespace Transfers.Domain.Transfers;

public enum TransferStatus
{
    /// <summary>« غير مسلمة » : créé, pas encore remis au bénéficiaire.</summary>
    NotDelivered,

    /// <summary>« مسلمة » : le bénéficiaire a reçu son argent. Verrouillé.</summary>
    Delivered,

    /// <summary>« ملغى » : annulé (motif obligatoire). Verrouillé.</summary>
    Cancelled,
}

/// <summary>Étape d'une ligne de répartition : paiement du client à la création, ou remise au bénéficiaire.</summary>
public enum PaymentStage
{
    Creation,
    Delivery,
}

/// <summary>Une ligne demandée de la répartition : un compte de trésorerie et un montant.</summary>
public sealed record PaymentLine(Guid TreasuryAccountId, Money Amount);

/// <summary>
/// Une ligne de la « répartition des paiements » (validé) : quelle méthode de paiement (caisse, Bankily, banque…)
/// a reçu ou versé quelle part du transfert.
/// </summary>
public sealed class TransferPayment
{
    // Constructeur réservé à EF Core.
    private TransferPayment()
    {
    }

    internal TransferPayment(PaymentStage stage, Guid treasuryAccountId, Money amount, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Stage = stage;
        TreasuryAccountId = treasuryAccountId;
        Amount = amount.Amount;
        Currency = amount.Currency;
    }

    public Guid Id { get; private set; }

    public PaymentStage Stage { get; private set; }

    public Guid TreasuryAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }
}
