using Hawala.SharedKernel.Messaging;

namespace Cash.Application.Features.Movements.ListCashMovements;

/// <summary>
/// Liste des mouvements de caisse : période sur la date d'opération (aujourd'hui par défaut), du plus récent au plus ancien,
/// 50 par page ; totaux des entrées et sorties de la période. Lecture avec cash.budget.read.
/// </summary>
/// <param name="AgencyId">Superviseur : l'agence choisie (les deux sinon) ; un agent ne voit que la sienne.</param>
/// <param name="Type">Receipt, Payment, OwnerContribution, OwnerWithdrawal ou InternalTransfer.</param>
/// <param name="TreasuryAccountId">Mouvements de ce compte (comme origine ou, pour un virement, comme destination).</param>
/// <param name="Search">Texte cherché dans la note.</param>
public sealed record ListCashMovementsQuery(
    Guid ActorId,
    Guid? AgencyId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Type = null,
    Guid? TreasuryAccountId = null,
    string? Search = null,
    int? Page = null) : IQuery<CashMovementList>;
