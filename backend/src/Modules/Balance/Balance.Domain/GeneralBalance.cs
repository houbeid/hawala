using Hawala.SharedKernel.Money;

namespace Balance.Domain;

/// <summary>
/// Composants du budget d'une caisse, dans sa devise (lus à la Caisse), avec les apports nets du patron de cette caisse.
/// Seulement des totaux : le détail par tiers reste sur l'écran des tiers (décision du 31 octobre 2026).
/// </summary>
/// <param name="Debtors">Créances : tiers « dans le bilan » qui nous doivent (positif).</param>
/// <param name="Creditors">Dettes : tiers « dans le bilan » à qui l'on doit (positif).</param>
/// <param name="Undelivered">Transferts que la caisse doit encore payer.</param>
/// <param name="OwnerContributions">Approvisionnements − retraits du patron dans cette caisse.</param>
public sealed record CaisseComponents(
    Currency Currency,
    decimal Cash,
    decimal Banks,
    decimal Debtors,
    decimal Creditors,
    decimal Undelivered,
    decimal OwnerContributions)
{
    /// <summary>Bilan de la caisse = son budget : espèces + banques + créances − dettes − non livrés.</summary>
    public decimal Budget => Cash + Banks + Debtors - Creditors - Undelivered;

    /// <summary>Les mêmes composants convertis en MRO (FCFA × taux ÷ 5 000, pleine précision) ; une caisse en MRO reste telle quelle.</summary>
    public CaisseComponents ToMro(ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        if (Currency == Currency.MRO)
        {
            return this;
        }

        return new CaisseComponents(
            Currency.MRO,
            GeneralBalanceFigures.ToMro(Cash, rate),
            GeneralBalanceFigures.ToMro(Banks, rate),
            GeneralBalanceFigures.ToMro(Debtors, rate),
            GeneralBalanceFigures.ToMro(Creditors, rate),
            GeneralBalanceFigures.ToMro(Undelivered, rate),
            GeneralBalanceFigures.ToMro(OwnerContributions, rate));
    }
}

/// <summary>
/// Bilan général (décisions du 31 octobre 2026, à côté du bilan par caisse qui reste) : la caisse de Nouakchott en MRO et celle
/// de Dakar convertie au taux du bilan (« 5 000 FCFA = ? MRO », prérempli au prix d'achat du jour, modifiable) ;
/// bilan général = bilan Nouakchott + bilan Dakar converti ; capital pris en compte = capital général (saisi en MRO) + apports
/// nets du patron des deux caisses (Dakar converti) ; résultat net = bilan général − capital pris en compte, et son pourcentage.
/// Pleine précision : seul l'affichage arrondit (règle d'arrondi 4).
/// </summary>
/// <param name="Nouakchott">Caisse en MRO, telle quelle.</param>
/// <param name="Dakar">Caisse en FCFA, dans sa devise.</param>
/// <param name="DakarInMro">Caisse de Dakar convertie au taux du bilan.</param>
/// <param name="Total">Composants des deux caisses additionnés, en MRO.</param>
/// <param name="GeneralBalance">Bilan général, en MRO.</param>
/// <param name="Capital">Capital général en MRO ; null s'il n'a pas été saisi (alors ni résultat ni pourcentage).</param>
public sealed record GeneralBalanceFigures(
    ExchangeRate Rate,
    CaisseComponents Nouakchott,
    CaisseComponents Dakar,
    CaisseComponents DakarInMro,
    CaisseComponents Total,
    decimal GeneralBalance,
    decimal? Capital,
    decimal OwnerContributionsMro,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent)
{
    public static GeneralBalanceFigures Compute(CaisseComponents nouakchott, CaisseComponents dakar, ExchangeRate rate, decimal? capital)
    {
        ArgumentNullException.ThrowIfNull(nouakchott);
        ArgumentNullException.ThrowIfNull(dakar);
        ArgumentNullException.ThrowIfNull(rate);
        if (nouakchott.Currency != Currency.MRO || dakar.Currency != Currency.XOF)
        {
            throw new ArgumentException("Le bilan général additionne la caisse en MRO et la caisse en FCFA.");
        }

        var dakarInMro = dakar.ToMro(rate);
        var total = new CaisseComponents(
            Currency.MRO,
            nouakchott.Cash + dakarInMro.Cash,
            nouakchott.Banks + dakarInMro.Banks,
            nouakchott.Debtors + dakarInMro.Debtors,
            nouakchott.Creditors + dakarInMro.Creditors,
            nouakchott.Undelivered + dakarInMro.Undelivered,
            nouakchott.OwnerContributions + dakarInMro.OwnerContributions);

        var general = nouakchott.Budget + dakarInMro.Budget;
        var capitalUsed = capital + total.OwnerContributions;
        var net = general - capitalUsed;
        decimal? percent = capitalUsed is { } c && c != 0m ? net / c * 100m : null;

        return new GeneralBalanceFigures(
            rate, nouakchott, dakar, dakarInMro, total, general, capital, total.OwnerContributions, capitalUsed, net, percent);
    }

    /// <summary>FCFA × taux ÷ 5 000, sans arrondi.</summary>
    public static decimal ToMro(decimal xof, ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return xof * rate.MroPer5000Xof / ExchangeRate.XofUnit;
    }
}
