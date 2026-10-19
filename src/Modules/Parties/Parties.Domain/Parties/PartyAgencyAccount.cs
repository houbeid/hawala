namespace Parties.Domain.Parties;

/// <summary>
/// La part d'un tiers dans une agence : son compte au grand livre (qui porte le solde, dans la devise de l'agence),
/// son solde initial et son plafond de crédit. Règle validée : un tiers est commun aux deux agences, avec un solde par agence.
/// </summary>
public sealed class PartyAgencyAccount
{
    // Constructeur réservé à EF Core.
    private PartyAgencyAccount()
    {
    }

    internal PartyAgencyAccount(Guid agencyId, Guid ledgerAccountId)
    {
        AgencyId = agencyId;
        LedgerAccountId = ledgerAccountId;
    }

    public Guid AgencyId { get; private set; }

    public Guid LedgerAccountId { get; private set; }

    /// <summary>
    /// Solde initial déjà passé au grand livre (positif = rouge, il nous doit ; négatif = vert), dans la devise de l'agence.
    /// Retenu ici pour calculer la différence quand le Superviseur le change : seule cette différence est écrite.
    /// Le solde réel, lui, se lit toujours au grand livre.
    /// </summary>
    public decimal OpeningBalance { get; private set; }

    /// <summary>
    /// Plafond de crédit dans la devise de l'agence ; null = pas de limite (règle validée).
    /// Fixé par le Superviseur ; bloquant pour le crédit, appliqué en phase 3 (transferts).
    /// </summary>
    public decimal? CreditLimit { get; private set; }

    /// <returns>La différence à passer au grand livre (nouveau − ancien) ; zéro si rien ne change.</returns>
    internal decimal ChangeOpeningBalance(decimal newOpeningBalance)
    {
        var difference = newOpeningBalance - OpeningBalance;
        OpeningBalance = newOpeningBalance;
        return difference;
    }
}
