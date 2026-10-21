using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.GetTransferHistory;

/// <summary>
/// Historique (règle validée le 21 octobre 2026) : visible de tous ceux qui voient le transfert ; les champs qu'un lecteur
/// n'a pas le droit de voir sont retirés de l'avant / après (répartition et crédit pour l'agence de destination,
/// prix d'achat et bénéfice sans profit.view).
/// POINT OUVERT : l'auteur est renvoyé par son id ; son nom viendra d'une lecture des comptes (Identity.Contracts).
/// </summary>
public sealed class GetTransferHistoryQueryHandler(
    ITransferRepository transfers,
    IAgencyScope scope,
    IPermissionChecker permissions)
    : IQueryHandler<GetTransferHistoryQuery, IReadOnlyList<TransferEventView>>
{
    /// <summary>Champs de la répartition et du crédit : agence d'envoi et Superviseur seulement.</summary>
    public static readonly IReadOnlySet<string> SendingDetailFields =
        new HashSet<string>(StringComparer.Ordinal) { "Payments", "CreditPartyId", "CreditAmount" };

    /// <summary>Champs du bénéfice : profit.view seulement.</summary>
    public static readonly IReadOnlySet<string> ProfitFields =
        new HashSet<string>(StringComparer.Ordinal) { "PurchaseRate", "ProfitMro" };

    public async Task<Result<IReadOnlyList<TransferEventView>>> Handle(GetTransferHistoryQuery query, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetByIdAsync(query.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var seesDetails = scope.SeesAllAgencies || scope.AgencyId == transfer.SourceAgencyId;
        var seesProfit = await permissions.HasPermissionAsync(query.ActorId, Permissions.ProfitView, cancellationToken);

        bool Visible(TransferFieldChange change) =>
            (seesDetails || !SendingDetailFields.Contains(change.Field))
            && (seesProfit || !ProfitFields.Contains(change.Field));

        return transfer.Events
            .OrderBy(e => e.At)
            .Select(e => new TransferEventView(e.Type, e.At, e.AuthorId, e.Reason, [.. e.Changes.Where(Visible)]))
            .ToList();
    }
}
