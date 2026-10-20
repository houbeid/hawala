using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Rates.Application.Abstractions;
using Rates.Domain;
using Rates.Domain.DailyRates;

namespace Rates.Application.Features.SetDailyRate;

/// <summary>
/// Crée le taux de la date s'il n'existe pas, le change sinon. Une seule table : pas de transaction commune.
/// POINT OUVERT : l'historique des changements d'un même jour (avant / après) arrivera avec le module Audit ;
/// en attendant, seuls la dernière valeur, son auteur et son heure sont gardés.
/// </summary>
public sealed class SetDailyRateCommandHandler(
    IDailyRateRepository rates,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<SetDailyRateCommand>
{
    public async Task<Result> Handle(SetDailyRateCommand command, CancellationToken cancellationToken)
    {
        if (command.PurchaseRate is not { } purchase)
        {
            return RatesErrors.Rate.PurchaseInvalid;
        }

        if (command.SaleRate is not { } sale)
        {
            return RatesErrors.Rate.SaleInvalid;
        }

        var existing = await rates.GetAsync(command.Date, cancellationToken);
        if (existing is not null)
        {
            var changed = existing.Change(purchase, sale, command.ActorId, clock.UtcNow);
            if (changed.IsFailure)
            {
                return changed.Error;
            }
        }
        else
        {
            var created = DailyRate.Create(command.Date, purchase, sale, command.ActorId, clock.UtcNow);
            if (created.IsFailure)
            {
                return created.Error;
            }

            rates.Add(created.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
