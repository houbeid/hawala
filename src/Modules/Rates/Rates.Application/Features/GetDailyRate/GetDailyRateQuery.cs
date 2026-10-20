using Hawala.SharedKernel.Messaging;

namespace Rates.Application.Features.GetDailyRate;

/// <summary>Les taux d'une journée ; aujourd'hui si aucune date n'est donnée.</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : décide si le prix d'achat est montré.</param>
public sealed record GetDailyRateQuery(Guid ActorId, DateOnly? Date = null) : IQuery<DailyRateView>;

/// <summary>Historique des taux entre deux dates ; le mois en cours si elles ne sont pas données.</summary>
public sealed record ListDailyRatesQuery(Guid ActorId, DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<DailyRateView>>;
