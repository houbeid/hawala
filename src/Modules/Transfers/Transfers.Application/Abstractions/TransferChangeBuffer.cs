namespace Transfers.Application.Abstractions;

/// <summary>
/// Notifications d'une requête, gardées jusqu'à la validation de la transaction : remplies à l'enregistrement
/// des transferts, envoyées si la commande réussit, oubliées sinon. Une instance par requête.
/// </summary>
public sealed class TransferChangeBuffer
{
    private readonly Dictionary<Guid, TransferChanged> _changes = [];

    /// <summary>Garde le dernier changement de chaque transfert.</summary>
    public void Add(TransferChanged change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _changes[change.TransferId] = change;
    }

    /// <summary>Vide le tampon et renvoie ce qu'il contenait.</summary>
    public IReadOnlyList<TransferChanged> TakeAll()
    {
        var all = _changes.Values.ToList();
        _changes.Clear();
        return all;
    }
}
