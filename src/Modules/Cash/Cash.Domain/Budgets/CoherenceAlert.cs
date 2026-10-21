namespace Cash.Domain.Budgets;

/// <summary>Alertes du contrôle de cohérence (validées le 21 octobre 2026) : à vérifier avant d'enregistrer le budget, sans blocage.</summary>
public enum CoherenceAlertKind
{
    /// <summary>Prix de vente d'un transfert à plus de 5 % du prix de vente du jour.</summary>
    UnusualRate,

    /// <summary>Ajustement de solde par le Superviseur.</summary>
    BalanceAdjustment,

    /// <summary>Solde initial d'un tiers changé.</summary>
    OpeningBalanceChanged,

    /// <summary>Mouvement de caisse annulé.</summary>
    CashMovementCancelled,

    /// <summary>Transfert annulé.</summary>
    TransferCancelled,

    /// <summary>Transfert livré sans code.</summary>
    DeliveredWithoutCode,

    /// <summary>Compte de trésorerie en négatif.</summary>
    NegativeBalance,
}

/// <param name="At">Instant de l'événement (ou du contrôle, pour un solde négatif).</param>
/// <param name="SourceId">Objet concerné : transfert, mouvement, ajustement, tiers ou compte.</param>
/// <param name="Label">Texte court lisible (référence, nom du compte ou du tiers, libellé de l'écriture).</param>
/// <param name="Amount">Montant concerné dans la devise de l'agence, ou taux du transfert ; null si sans objet.</param>
/// <param name="Reason">Motif enregistré (annulation, ajustement, livraison sans code) ; null sinon.</param>
public sealed record CoherenceAlert(
    CoherenceAlertKind Kind,
    DateTimeOffset At,
    Guid? SourceId,
    string Label,
    decimal? Amount,
    string? Reason);
