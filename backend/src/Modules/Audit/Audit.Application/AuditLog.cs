using Audit.Application.Abstractions;
using Audit.Contracts;
using Audit.Domain;
using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;

namespace Audit.Application;

/// <summary>
/// Écrit une ligne d'audit dans la transaction commune de la commande appelante.
/// Refuse d'écrire hors transaction : sans elle, la ligne serait validée tout de suite,
/// même si la commande échouait ensuite (une action tracée qui n'a jamais eu lieu).
/// </summary>
public sealed class AuditLog(IAuditStore store, IAuditRequestContext request, IClock clock) : IAuditLog
{
    public async Task<Result<Guid>> RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!store.IsInSharedTransaction)
        {
            throw new InvalidOperationException(
                $"Audit « {record.Action} » hors transaction commune : enregistrer la commande avec AddSharedTransactional.");
        }

        var now = clock.UtcNow;
        var entry = AuditEntry.Create(IdGenerator.NewId(now), now, record, request.IpAddress, request.UserAgent);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        store.Add(entry.Value);
        await store.SaveChangesAsync(cancellationToken);

        return entry.Value.Id;
    }
}
