using Parties.Domain.Scopes;

namespace Parties.Application.Abstractions;

/// <summary>Tiers autorisés des comptes limités. Enregistrement par <see cref="IUnitOfWork"/>.</summary>
public interface IPartyScopeRepository
{
    /// <summary>La règle d'un compte (suivie), ou null s'il n'en a jamais eu (= non limité).</summary>
    Task<AccountPartyScope?> GetAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Lecture seule pour le filtre de la requête : limité ou non, et ses tiers autorisés.</summary>
    Task<VisibleParties> GetVisibleAsync(Guid accountId, CancellationToken cancellationToken);

    void Add(AccountPartyScope scope);
}

/// <summary>Ce qu'un compte voit des tiers : tout, ou seulement une liste.</summary>
public sealed record VisibleParties(bool IsLimited, IReadOnlyCollection<Guid> AllowedPartyIds)
{
    public static readonly VisibleParties All = new(false, []);

    public bool Allows(Guid partyId) => !IsLimited || AllowedPartyIds.Contains(partyId);
}
