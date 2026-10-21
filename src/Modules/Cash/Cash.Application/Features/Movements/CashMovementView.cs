using Cash.Domain.Movements;
using Hawala.SharedKernel.Money;
using Parties.Contracts;
using Treasury.Contracts;

namespace Cash.Application.Features.Movements;

/// <summary>Un mouvement tel qu'on le montre, avec les noms de la méthode de paiement et du tiers.</summary>
/// <param name="Incoming">Vrai si l'argent entre (encaissement, approvisionnement) ; faux s'il sort.</param>
public sealed record CashMovementView(
    Guid Id,
    Guid AgencyId,
    CashMovementType Type,
    bool Incoming,
    Guid TreasuryAccountId,
    string? TreasuryAccountName,
    Guid? PartyId,
    string? PartyName,
    decimal Amount,
    Currency Currency,
    string? Note,
    DateTimeOffset OperationAt,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);

/// <summary>Total et nombre de mouvements.</summary>
public sealed record CashAmount(decimal Amount, int Count);

/// <summary>
/// Une page de la liste (écran « Caisse » de l'app actuelle : « 0 / 0 result(s) », badges vert / rouge, total).
/// </summary>
/// <param name="Incoming">Entrées de la période (encaissements et approvisionnements).</param>
/// <param name="Outgoing">Sorties de la période (décaissements et retraits du patron).</param>
/// <param name="Net">Entrées − sorties.</param>
public sealed record CashMovementList(
    IReadOnlyList<CashMovementView> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int AllCount,
    CashAmount Incoming,
    CashAmount Outgoing,
    decimal Net);

/// <summary>Construit les vues en lisant en une fois les noms des comptes et des tiers.</summary>
public sealed class CashMovementViews(ITreasuryDirectory treasury, IPartyDirectory parties)
{
    public async Task<IReadOnlyList<CashMovementView>> BuildAsync(
        IReadOnlyList<CashMovement> movements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movements);
        if (movements.Count == 0)
        {
            return [];
        }

        var accounts = await treasury.GetByIdsAsync([.. movements.Select(m => m.TreasuryAccountId).Distinct()], cancellationToken);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
        var partyNames = await parties.GetNamesAsync(
            [.. movements.Where(m => m.PartyId is not null).Select(m => m.PartyId!.Value).Distinct()], cancellationToken);

        return [.. movements.Select(m => new CashMovementView(
            m.Id, m.AgencyId, m.Type, m.IsIncoming,
            m.TreasuryAccountId, accountNames.GetValueOrDefault(m.TreasuryAccountId),
            m.PartyId, m.PartyId is { } id ? partyNames.GetValueOrDefault(id) : null,
            m.Amount, m.Currency, m.Note, m.OperationAt, m.CreatedAt, m.CreatedBy))];
    }
}
