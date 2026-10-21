using Parties.Application.Abstractions;
using Parties.Contracts;

namespace Parties.Application.Features.Parties;

/// <summary>Implémentation de la porte publique des noms de tiers.</summary>
public sealed class PartyDirectory(IPartyRepository parties) : IPartyDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var found = await parties.GetByIdsAsync(ids, cancellationToken);
        return found.ToDictionary(p => p.Id, p => p.Name);
    }
}
