using Audit.Contracts;
using Hawala.SharedKernel.Results;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers;

/// <summary>
/// Corrections d'un transfert tracées dans l'audit (phase 6, tour 7c, règle du 22 octobre 2026) : annulation, modification
/// d'argent, livraison sans code, déblocage du code, nouveau code. Les opérations courantes (création, livraison, textes)
/// restent seulement dans l'historique du transfert et au grand livre.
/// La ligne reprend la dernière ligne d'historique (motif, avant / après) : jamais le code, ni la pièce d'identité.
/// </summary>
internal static class TransferAudit
{
    public const string Cancelled = "Transfers.Transfer.Cancelled";
    public const string AmountsEdited = "Transfers.Transfer.AmountsEdited";
    public const string DeliveredWithoutCode = "Transfers.Transfer.DeliveredWithoutCode";
    public const string CodeUnlocked = "Transfers.Transfer.CodeUnlocked";
    public const string CodeReplaced = "Transfers.Transfer.CodeReplaced";

    /// <param name="agencyId">Agence concernée : celle d'envoi (annulation, argent, nouveau code) ou de destination (livraison, déblocage).</param>
    public static async Task<Result> RecordAsync(
        IAuditLog audit, string action, Transfer transfer, Guid actorId, Guid agencyId, CancellationToken cancellationToken)
    {
        var last = transfer.Events.Count == 0 ? null : transfer.Events[^1];
        var changes = (last?.Changes ?? [])
            .Where(c => !string.Equals(c.Before, c.After, StringComparison.Ordinal))
            .Select(c => new AuditChange(c.Field, c.Before, c.After))
            .ToList();

        var recorded = await audit.RecordAsync(
            new AuditRecord(action, "Transfer", transfer.Id, actorId, agencyId, last?.Reason, changes), cancellationToken);

        return recorded.IsFailure ? recorded.Error : Result.Success();
    }
}
