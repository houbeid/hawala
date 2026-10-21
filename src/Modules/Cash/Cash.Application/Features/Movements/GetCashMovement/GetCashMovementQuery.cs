using Cash.Application.Abstractions;
using Cash.Domain;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;

namespace Cash.Application.Features.Movements.GetCashMovement;

/// <summary>Fiche d'un mouvement de son agence (les deux pour le Superviseur) ; hors périmètre = 404.</summary>
public sealed record GetCashMovementQuery(Guid ActorId, Guid MovementId) : IQuery<CashMovementView>;

public sealed class GetCashMovementQueryHandler(ICashMovementRepository movements, CashMovementViews views)
    : IQueryHandler<GetCashMovementQuery, CashMovementView>
{
    public async Task<Result<CashMovementView>> Handle(GetCashMovementQuery query, CancellationToken cancellationToken)
    {
        var movement = await movements.GetByIdAsync(query.MovementId, cancellationToken);
        if (movement is null)
        {
            return CashErrors.Movement.NotFound;
        }

        return (await views.BuildAsync([movement], cancellationToken))[0];
    }
}
