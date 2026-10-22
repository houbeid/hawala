namespace Parties.Domain.Scopes;

/// <summary>Un tiers autorisé pour un compte limité.</summary>
public sealed record AllowedParty(Guid PartyId);

/// <summary>
/// « Limiter les tiers » d'un compte (phase 6, règles validées le 21 octobre 2026) : un compte limité ne voit
/// que ses tiers autorisés (liste, fiche, relevé) et ne peut faire crédit ou mouvement de caisse qu'avec eux ;
/// un tiers qu'il crée entre dans sa liste. Les transferts restent filtrés par agence seulement.
/// Gardé dans le module Tiers (qui connaît les tiers) ; l'id du compte vient du module Identité.
/// Un Superviseur n'est jamais limité : l'appelant le vérifie, et la lecture l'ignore pour lui.
/// </summary>
public sealed class AccountPartyScope
{
    // Rempli par EF Core au chargement : pas readonly.
    private List<AllowedParty> _allowedParties = [];

    // Constructeur réservé à EF Core.
    private AccountPartyScope()
    {
    }

    private AccountPartyScope(Guid accountId, DateTimeOffset now)
    {
        AccountId = accountId;
        UpdatedAt = now;
    }

    public Guid AccountId { get; private set; }

    public bool IsLimited { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AllowedParty> AllowedParties => _allowedParties;

    /// <summary>Un compte sans règle n'est pas limité.</summary>
    public static AccountPartyScope Create(Guid accountId, DateTimeOffset now) => new(accountId, now);

    public bool Allows(Guid partyId) => !IsLimited || _allowedParties.Exists(p => p.PartyId == partyId);

    /// <summary>
    /// État complet fixé par le Superviseur. Non limité : la liste est vidée (elle ne servirait à rien).
    /// Les tiers gardés restent les mêmes objets (suivis par EF) ; seuls les autres sont retirés ou ajoutés.
    /// </summary>
    /// <returns>Vrai si quelque chose a changé.</returns>
    public bool Set(bool limited, IEnumerable<Guid> partyIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(partyIds);

        HashSet<Guid> wanted = limited ? [.. partyIds] : [];
        var current = _allowedParties.Select(p => p.PartyId).ToHashSet();
        if (limited == IsLimited && wanted.SetEquals(current))
        {
            return false;
        }

        IsLimited = limited;
        _allowedParties.RemoveAll(p => !wanted.Contains(p.PartyId));
        _allowedParties.AddRange(wanted.Where(id => !current.Contains(id)).Select(id => new AllowedParty(id)));
        UpdatedAt = now;
        return true;
    }

    /// <summary>Un compte limité qui crée un tiers le voit ensuite (règle validée) ; sans effet s'il n'est pas limité.</summary>
    public bool Allow(Guid partyId, DateTimeOffset now)
    {
        if (!IsLimited || _allowedParties.Exists(p => p.PartyId == partyId))
        {
            return false;
        }

        _allowedParties.Add(new AllowedParty(partyId));
        UpdatedAt = now;
        return true;
    }
}
