namespace Parties.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers le compte d'un tiers dans une agence, pour lui faire crédit
/// (ex. les Transferts, quand le crédit est activé). Un tiers est commun aux deux agences : pas de filtre de périmètre ici,
/// c'est l'appelant qui choisit l'agence (celle de l'envoi).
/// </summary>
public interface IPartyCreditAccounts
{
    /// <summary>
    /// Le compte du tiers dans l'agence, VERROUILLÉ jusqu'à la fin de la transaction en cours : deux crédits simultanés
    /// sur le même tiers passent l'un après l'autre, et le contrôle du plafond lit un solde à jour. Null si le tiers n'existe pas.
    /// </summary>
    Task<PartyCreditAccount?> LockForCreditAsync(Guid partyId, Guid agencyId, CancellationToken cancellationToken);
}

/// <param name="LedgerAccountId">Compte au grand livre du tiers dans cette agence (débité de la part à crédit).</param>
/// <param name="CreditLimit">Plafond dans la devise de l'agence : 0 = aucun crédit, null = pas de plafond.</param>
public sealed record PartyCreditAccount(
    Guid PartyId,
    string Name,
    bool IsArchived,
    Guid LedgerAccountId,
    decimal? CreditLimit);
