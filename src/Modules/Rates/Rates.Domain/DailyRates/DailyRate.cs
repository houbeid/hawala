using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Rates.Domain.DailyRates;

/// <summary>
/// Les deux taux d'une journée, au format « 5 000 FCFA = ? MRO ». Règles validées :
/// - prix d'achat = taux du marché (T), ce que coûtent les FCFA ; prix de vente = taux appliqué au client ;
/// - un seul taux du jour pour les deux agences (un seul corridor) ;
/// - fixés et changés par le Superviseur seul, à tout moment : les transferts déjà créés gardent leurs taux ;
/// - au plus 2 décimales, refusé au-delà (jamais arrondi).
/// POINT OUVERT : aucune règle n'impose prix de vente ≥ prix d'achat (une commission négative reste possible) ;
/// fixer le taux d'une date future (à l'avance) est permis, faute de règle contraire.
/// </summary>
public sealed class DailyRate
{
    // Constructeur réservé à EF Core.
    private DailyRate()
    {
        PurchaseRate = null!;
        SaleRate = null!;
    }

    private DailyRate(DateOnly date, ExchangeRate purchaseRate, ExchangeRate saleRate, Guid authorId, DateTimeOffset now)
    {
        Date = date;
        PurchaseRate = purchaseRate;
        SaleRate = saleRate;
        UpdatedBy = authorId;
        UpdatedAt = now;
    }

    /// <summary>La journée concernée ; un seul taux par date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Prix d'achat (taux du marché, T) : réservé au Superviseur à l'affichage.</summary>
    public ExchangeRate PurchaseRate { get; private set; }

    /// <summary>Prix de vente : appliqué automatiquement à chaque nouveau transfert de la journée.</summary>
    public ExchangeRate SaleRate { get; private set; }

    /// <summary>Dernier Superviseur à avoir fixé ces taux.</summary>
    public Guid UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<DailyRate> Create(
        DateOnly date, decimal purchaseRate, decimal saleRate, Guid authorId, DateTimeOffset now)
    {
        var rates = Validate(purchaseRate, saleRate, authorId);
        if (rates.IsFailure)
        {
            return rates.Error;
        }

        return new DailyRate(date, rates.Value.Purchase, rates.Value.Sale, authorId, now);
    }

    /// <summary>Le Superviseur change les taux de la journée ; seuls les nouveaux transferts les prendront.</summary>
    public Result Change(decimal purchaseRate, decimal saleRate, Guid authorId, DateTimeOffset now)
    {
        var rates = Validate(purchaseRate, saleRate, authorId);
        if (rates.IsFailure)
        {
            return rates.Error;
        }

        PurchaseRate = rates.Value.Purchase;
        SaleRate = rates.Value.Sale;
        UpdatedBy = authorId;
        UpdatedAt = now;
        return Result.Success();
    }

    private static Result<(ExchangeRate Purchase, ExchangeRate Sale)> Validate(decimal purchaseRate, decimal saleRate, Guid authorId)
    {
        if (authorId == Guid.Empty)
        {
            return RatesErrors.Rate.AuthorRequired;
        }

        var purchase = ExchangeRate.Create(purchaseRate);
        if (purchase.IsFailure)
        {
            return RatesErrors.Rate.PurchaseInvalid;
        }

        var sale = ExchangeRate.Create(saleRate);
        if (sale.IsFailure)
        {
            return RatesErrors.Rate.SaleInvalid;
        }

        return (purchase.Value, sale.Value);
    }
}
