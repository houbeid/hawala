using Hawala.SharedKernel.Money;

namespace Organization.Contracts;

/// <summary>
/// Ce que les autres modules (et l'API) savent d'une agence. Ne dépend pas du Domain d'Organisation :
/// le pays est donné en texte (« Mauritania », « Senegal »).
/// </summary>
public sealed record AgencySummary(
    Guid Id,
    string Code,
    string Name,
    string Country,
    Currency Currency,
    string Color,
    string TimeZoneId);