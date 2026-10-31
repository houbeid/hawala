using Hawala.SharedKernel.Money;

namespace Balance.Domain;

/// <summary>Les composants du budget d'une caisse, dans l'ordre de l'écran.</summary>
public enum BalanceComponent
{
    Cash,
    Banks,
    Debtors,
    Creditors,
    Undelivered,
}

/// <summary>Variation d'un composant entre la photo et maintenant.</summary>
/// <param name="NouakchottChange">Écart côté Nouakchott, en MRO.</param>
/// <param name="DakarChange">Écart côté Dakar, en FCFA.</param>
/// <param name="DakarChangeMro">Écart côté Dakar converti au taux de maintenant.</param>
/// <param name="Effect">Effet sur le bilan général, en MRO : + pour espèces, banques, créances ; − pour dettes et non livrés.</param>
public sealed record ComponentVariation(
    BalanceComponent Component, decimal NouakchottChange, decimal DakarChange, decimal DakarChangeMro, decimal Effect);

/// <summary>
/// « Expliquer la variation » (décision du 31 octobre 2026) : écart entre une photo du bilan général et maintenant, poste par
/// poste et par caisse, plus l'effet du changement de taux. Décomposition exacte, en pleine précision :
/// écart du bilan général = Σ effets des composants (Dakar converti au taux de maintenant) + bilan de Dakar de la photo ×
/// (taux de maintenant − taux de la photo) ÷ 5 000. Le capital pris en compte et le résultat net sont comparés à part.
/// </summary>
/// <param name="RateEffect">Effet du seul changement de taux sur le bilan de Dakar de la photo, en MRO.</param>
/// <param name="CapitalChange">Écart du capital général ; null si l'un des deux n'est pas saisi.</param>
/// <param name="OwnerContributionsChange">Écart des apports nets du patron des deux caisses, en MRO (chacun à son taux).</param>
/// <param name="NetResultChange">Écart du résultat net ; null si l'un des deux n'existe pas.</param>
public sealed record GeneralBalanceVariation(
    decimal FromRate,
    decimal ToRate,
    decimal FromGeneralBalance,
    decimal ToGeneralBalance,
    IReadOnlyList<ComponentVariation> Components,
    decimal RateEffect,
    decimal GeneralBalanceChange,
    decimal? CapitalChange,
    decimal OwnerContributionsChange,
    decimal? NetResultChange)
{
    public static GeneralBalanceVariation Between(GeneralBalanceFigures before, GeneralBalanceFigures after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var components = new List<ComponentVariation>();
        foreach (var component in Enum.GetValues<BalanceComponent>())
        {
            var nouakchott = Of(after.Nouakchott, component) - Of(before.Nouakchott, component);
            var dakar = Of(after.Dakar, component) - Of(before.Dakar, component);
            var dakarMro = GeneralBalanceFigures.ToMro(dakar, after.Rate);
            var sign = component is BalanceComponent.Creditors or BalanceComponent.Undelivered ? -1m : 1m;
            components.Add(new ComponentVariation(component, nouakchott, dakar, dakarMro, sign * (nouakchott + dakarMro)));
        }

        var rateEffect = before.Dakar.Budget * (after.Rate.MroPer5000Xof - before.Rate.MroPer5000Xof) / ExchangeRate.XofUnit;

        return new GeneralBalanceVariation(
            before.Rate.MroPer5000Xof,
            after.Rate.MroPer5000Xof,
            before.GeneralBalance,
            after.GeneralBalance,
            components,
            rateEffect,
            after.GeneralBalance - before.GeneralBalance,
            after.Capital - before.Capital,
            after.OwnerContributionsMro - before.OwnerContributionsMro,
            after.NetResult - before.NetResult);
    }

    private static decimal Of(CaisseComponents c, BalanceComponent component) => component switch
    {
        BalanceComponent.Cash => c.Cash,
        BalanceComponent.Banks => c.Banks,
        BalanceComponent.Debtors => c.Debtors,
        BalanceComponent.Creditors => c.Creditors,
        _ => c.Undelivered,
    };
}
