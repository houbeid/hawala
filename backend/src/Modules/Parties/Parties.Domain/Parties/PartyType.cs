namespace Parties.Domain.Parties;

/// <summary>
/// Type de tiers. Règle validée : il donne aussi le compte comptable (jamais saisi à part).
/// </summary>
public enum PartyType
{
    /// <summary>Compte comptable 41.</summary>
    Client,

    /// <summary>Compte comptable 40.</summary>
    Supplier,

    /// <summary>Compte comptable 42.</summary>
    Staff,

    /// <summary>Sans compte comptable dédié.</summary>
    Other,
}

public static class PartyTypes
{
    /// <summary>Compte comptable du type : 41 (clients), 40 (fournisseurs), 42 (personnel), aucun pour « Autre ».</summary>
    public static string? AccountCode(this PartyType type) => type switch
    {
        PartyType.Client => "41",
        PartyType.Supplier => "40",
        PartyType.Staff => "42",
        PartyType.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Type de tiers inconnu."),
    };
}
