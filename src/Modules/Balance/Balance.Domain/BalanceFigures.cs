using Hawala.SharedKernel.Money;

namespace Balance.Domain;

/// <summary>Ce que le bilan lit ailleurs : les deux budgets, le capital et les apports du patron, chacun dans sa devise.</summary>
/// <param name="MroBudget">Budget de l'agence en MRO (Nouakchott).</param>
/// <param name="XofBudget">Budget de l'agence en FCFA (Dakar).</param>
/// <param name="Rate">Taux du bilan (MRO pour 5 000 FCFA) : prix d'achat du jour par défaut, modifiable par le Superviseur.</param>
/// <param name="InitialCapital">Capital initial en MRO ; null s'il n'a pas encore été saisi.</param>
/// <param name="MroOwnerContributions">Approvisionnements − retraits du patron à Nouakchott (MRO).</param>
/// <param name="XofOwnerContributions">Approvisionnements − retraits du patron à Dakar (FCFA).</param>
public sealed record BalanceInputs(
    decimal MroBudget,
    decimal XofBudget,
    ExchangeRate Rate,
    decimal? InitialCapital,
    decimal MroOwnerContributions,
    decimal XofOwnerContributions);

/// <summary>
/// Bilan général (règles validées), tout en MRO au taux du bilan, en pleine précision (seul l'affichage arrondit) :
/// bilan général = budget de Nouakchott + budget de Dakar converti
///   (= trésorerie + résultat tiers − transferts non livrés, les budgets les soustrayant déjà) ;
/// capital pris en compte = capital initial + approvisionnements − retraits du patron (21 octobre 2026) ;
/// résultat net = bilan général − capital pris en compte, et son pourcentage du capital.
/// </summary>
public sealed record BalanceFigures(
    decimal MroBudget,
    decimal XofBudget,
    decimal XofBudgetInMro,
    decimal GeneralBalance,
    decimal? InitialCapital,
    decimal OwnerContributionsMro,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal Rate)
{
    public static BalanceFigures Compute(BalanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var rate = inputs.Rate.MroPer5000Xof;
        var xofInMro = inputs.XofBudget * rate / ExchangeRate.XofUnit;
        var general = inputs.MroBudget + xofInMro;
        var contributions = inputs.MroOwnerContributions + (inputs.XofOwnerContributions * rate / ExchangeRate.XofUnit);
        var capital = inputs.InitialCapital + contributions;
        var net = general - capital;
        decimal? percent = capital is { } c && c != 0m ? net / c * 100m : null;

        return new BalanceFigures(
            inputs.MroBudget, inputs.XofBudget, xofInMro, general, inputs.InitialCapital, contributions, capital, net, percent, rate);
    }
}
