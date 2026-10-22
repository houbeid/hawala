namespace Identity.Domain.Accounts;

/// <summary>
/// Plafonds d'un compte (phase 6, règles validées le 22 octobre 2026), dans la devise de son agence
/// (MRO à Nouakchott, FCFA à Dakar), sur le montant payé à l'agence d'envoi. Null = pas de limite.
/// </summary>
/// <param name="PerTransfer">Montant maximal d'un transfert.</param>
/// <param name="PerDay">Total maximal des transferts enregistrés par le compte dans la journée.</param>
/// <param name="CreditPerDay">Total maximal des parts à crédit accordées par le compte dans la journée.</param>
public sealed record AccountLimits(decimal? PerTransfer, decimal? PerDay, decimal? CreditPerDay)
{
    public static readonly AccountLimits None = new(null, null, null);
}
