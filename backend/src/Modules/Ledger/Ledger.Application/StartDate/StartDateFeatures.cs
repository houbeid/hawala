using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Application.Abstractions;
using Ledger.Domain;
using Ledger.Domain.Settings;

namespace Ledger.Application.StartDate;

/// <summary>Date de démarrage fixée (null tant qu'elle ne l'est pas), avec l'instant et l'auteur.</summary>
public sealed record StartDateView(DateOnly? StartDate, DateTimeOffset? SetAt, Guid? SetBy);

public sealed record GetStartDateQuery : IQuery<StartDateView>;

/// <param name="ActorId">Superviseur connecté (gardé avec la date : qui l'a fixée).</param>
public sealed record SetStartDateCommand(Guid ActorId, DateOnly? StartDate) : ICommand<StartDateView>;

public sealed class GetStartDateQueryHandler(ILedgerSettingsRepository settings) : IQueryHandler<GetStartDateQuery, StartDateView>
{
    public async Task<Result<StartDateView>> Handle(GetStartDateQuery query, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        return current is null
            ? new StartDateView(null, null, null)
            : new StartDateView(current.StartDate, current.StartDateSetAt, current.StartDateSetBy);
    }
}

/// <summary>
/// Fixe la date de démarrage, une seule fois (décision du 24 octobre 2026) : jamais future ; refusée si des écritures sont
/// déjà datées avant elle (la période de l'ancienne app doit rester vide dans cette app). Qui et quand sont gardés avec la date.
/// </summary>
public sealed class SetStartDateCommandHandler(
    ILedgerSettingsRepository settings,
    IJournalEntryRepository entries,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<SetStartDateCommand, StartDateView>
{
    public async Task<Result<StartDateView>> Handle(SetStartDateCommand command, CancellationToken cancellationToken)
    {
        if (command.StartDate is not { } date)
        {
            return LedgerErrors.StartDate.Required;
        }

        if (await settings.GetAsync(cancellationToken) is not null)
        {
            return LedgerErrors.StartDate.AlreadySet;
        }

        var now = clock.UtcNow;
        var fixedDate = LedgerSettings.FixStartDate(date, DateOnly.FromDateTime(now.UtcDateTime), command.ActorId, now);
        if (fixedDate.IsFailure)
        {
            return fixedDate.Error;
        }

        var before = await entries.CountBeforeAsync(date, cancellationToken);
        if (before > 0)
        {
            return LedgerErrors.StartDate.EntriesBefore(before);
        }

        settings.Add(fixedDate.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StartDateView(date, now, command.ActorId);
    }
}
