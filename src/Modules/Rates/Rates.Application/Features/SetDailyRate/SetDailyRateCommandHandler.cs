using System.Globalization;
using Audit.Contracts;
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
    IClock clock,
    IAuditLog audit)
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
        var before = existing is null ? ((decimal?)null, (decimal?)null) : (existing.PurchaseRate.MroPer5000Xof, existing.SaleRate.MroPer5000Xof);
        if (before == (purchase, sale))
        {
            return Result.Success();
        }

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

        // Tracé dans l'audit (phase 6, tour 7d) : un taux vaut pour les deux agences (pas d'agence sur la ligne) ;
        // la date est donnée en « date » (un taux n'a pas d'id : il est identifié par sa date).
        var audited = await audit.RecordAsync(
            new AuditRecord(
                "Rates.DailyRate.Set",
                "DailyRate",
                EntityId: null,
                command.ActorId,
                Changes: new[]
                    {
                        new AuditChange("date", null, command.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new AuditChange("purchaseRate", Text(before.Item1), Text(purchase)),
                        new AuditChange("saleRate", Text(before.Item2), Text(sale)),
                    }
                    .Where(c => !string.Equals(c.Before, c.After, StringComparison.Ordinal))
                    .ToList()),
            cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }

    private static string? Text(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);
}
