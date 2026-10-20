using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Rates.Application.Abstractions;
using Rates.Domain;

namespace Rates.Application.Features.GetDailyRate;

/// <summary>
/// Lecture des taux par tout compte connecté : le prix de vente pour tous, le prix d'achat seulement avec profit.view.
/// « Aujourd'hui » est la date UTC (Nouakchott et Dakar sont en UTC+0).
/// </summary>
public sealed class GetDailyRateQueryHandler(
    IDailyRateRepository rates,
    IPermissionChecker permissions,
    IClock clock)
    : IQueryHandler<GetDailyRateQuery, DailyRateView>
{
    public async Task<Result<DailyRateView>> Handle(GetDailyRateQuery query, CancellationToken cancellationToken)
    {
        var date = query.Date ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var rate = await rates.GetAsync(date, cancellationToken);
        if (rate is null)
        {
            return RatesErrors.Rate.NotFound;
        }

        var canSeePurchase = await permissions.HasPermissionAsync(query.ActorId, Permissions.ProfitView, cancellationToken);
        return DailyRateViewMapper.ToView(rate, canSeePurchase);
    }
}

public sealed class ListDailyRatesQueryHandler(
    IDailyRateRepository rates,
    IPermissionChecker permissions,
    IClock clock)
    : IQueryHandler<ListDailyRatesQuery, IReadOnlyList<DailyRateView>>
{
    public async Task<Result<IReadOnlyList<DailyRateView>>> Handle(ListDailyRatesQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = query.To ?? today;

        var canSeePurchase = await permissions.HasPermissionAsync(query.ActorId, Permissions.ProfitView, cancellationToken);
        var found = await rates.ListAsync(from, to, cancellationToken);

        return Result.Success<IReadOnlyList<DailyRateView>>([.. found.Select(r => DailyRateViewMapper.ToView(r, canSeePurchase))]);
    }
}
