using Hawala.SharedKernel.Ids;

namespace Transfers.Domain.Transfers;

/// <summary>Ce qui est arrivé au transfert (historique validé le 21 octobre 2026).</summary>
public enum TransferEventType
{
    Created,
    Edited,
    Delivered,
    DeliveredWithoutCode,
    Cancelled,
    CodeUnlocked,
    CodeReplaced,
    WrongCode,
}

/// <summary>Un champ changé : valeurs avant et après, en texte (invariant).</summary>
public sealed record TransferFieldChange(string Field, string? Before, string? After);

/// <summary>
/// Une ligne de l'historique d'un transfert : qui, quand, quoi, motif, et l'avant / après des champs changés.
/// Ajoutée seulement, jamais modifiée. La pièce d'identité d'une livraison sans code n'y figure jamais.
/// </summary>
public sealed class TransferEvent
{
    public const int ReasonMaxLength = 500;

    private List<TransferFieldChange> _changes = [];

    // Constructeur réservé à EF Core.
    private TransferEvent()
    {
    }

    internal TransferEvent(
        TransferEventType type, Guid authorId, DateTimeOffset at, string? reason, IReadOnlyList<TransferFieldChange> changes)
    {
        Id = IdGenerator.NewId(at);
        Type = type;
        AuthorId = authorId;
        At = at;
        Reason = reason;
        _changes = [.. changes];
    }

    public Guid Id { get; private set; }

    public TransferEventType Type { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string? Reason { get; private set; }

    public IReadOnlyList<TransferFieldChange> Changes => _changes;
}
