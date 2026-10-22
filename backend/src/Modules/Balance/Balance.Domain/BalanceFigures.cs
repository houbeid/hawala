using Hawala.SharedKernel.Money;

namespace Balance.Domain;

/// <summary>Ce que le bilan d'une caisse lit ailleurs, tout dans la devise de la caisse (aucune conversion).</summary>
/// <param name="Budget">Budget de la caisse (espèces + banques + débiteurs − créditeurs − non livrés).</param>
/// <param name="InitialCapital">Capital initial de la caisse ; null s'il n'a pas encore été saisi.</param>
/// <param name="OwnerContributions">Approvisionnements − retraits du patron dans cette caisse.</param>
public sealed record BalanceInputs(Currency Currency, decimal Budget, decimal? InitialCapital, decimal OwnerContributions);

/// <summary>
/// Bilan d'une caisse (décision du 21 octobre 2026 : « deux caisses, deux capitaux », sans total converti) :
/// capital pris en compte = capital initial de la caisse + approvisionnements − retraits du patron de cette caisse ;
/// résultat net = budget de la caisse − capital pris en compte, et son pourcentage du capital.
/// Tout dans la devise de la caisse : le taux du jour n'a aucun effet. Pleine précision, seul l'affichage arrondit.
/// </summary>
public sealed record BalanceFigures(
    Currency Currency,
    decimal Budget,
    decimal? InitialCapital,
    decimal OwnerContributions,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent)
{
    public static BalanceFigures Compute(BalanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var capital = inputs.InitialCapital + inputs.OwnerContributions;
        var net = inputs.Budget - capital;
        decimal? percent = capital is { } c && c != 0m ? net / c * 100m : null;

        return new BalanceFigures(inputs.Currency, inputs.Budget, inputs.InitialCapital, inputs.OwnerContributions, capital, net, percent);
    }
}
