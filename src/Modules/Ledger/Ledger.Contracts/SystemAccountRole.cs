namespace Ledger.Contracts;

/// <summary>
/// Comptes système, un de chaque par agence (donc par devise : MRO à Nouakchott, FCFA à Dakar).
/// Règle validée : créés à la demande, jamais saisis par un agent.
/// </summary>
public enum SystemAccountRole
{
    /// <summary>Passage MRO ↔ FCFA : garde chaque devise équilibrée dans une écriture de transfert.</summary>
    ExchangePosition,

    /// <summary>Transferts encaissés mais pas encore payés au bénéficiaire (soustraits du budget et du bilan).</summary>
    TransfersToDeliver,

    /// <summary>« Approvisionnement de caisse » et « Retrait du patron » : rattaché au capital, ni gain ni perte.</summary>
    OwnerContributions,

    /// <summary>« Écarts et ajustements » : correction d'un solde par le Superviseur, motif obligatoire.</summary>
    Adjustments,

    /// <summary>Contrepartie du solde initial d'un tiers : rattaché au capital, ni gain ni perte.</summary>
    OpeningBalances,
}
