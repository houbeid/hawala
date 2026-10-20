using Hawala.SharedKernel.Messaging;

namespace Rates.Application.Features.SetDailyRate;

/// <summary>
/// Le Superviseur fixe ou change les deux taux d'une journée (permission rates.set_daily).
/// Les transferts déjà créés gardent leurs taux ; seuls les nouveaux prendront ceux-ci.
/// </summary>
/// <param name="ActorId">Superviseur connecté, lu dans le JWT.</param>
/// <param name="PurchaseRate">Prix d'achat (taux du marché), MRO pour 5 000 FCFA.</param>
/// <param name="SaleRate">Prix de vente (appliqué au client), MRO pour 5 000 FCFA.</param>
public sealed record SetDailyRateCommand(Guid ActorId, DateOnly Date, decimal? PurchaseRate, decimal? SaleRate) : ICommand;
