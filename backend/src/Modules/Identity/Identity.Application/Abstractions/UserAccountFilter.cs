using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Application.Abstractions;

/// <summary>
/// Filtres de la liste des comptes. Un champ null = pas de filtre sur ce critère.
/// </summary>
/// <param name="Search">Texte cherché dans le nom, l'email ou le téléphone (sans tenir compte de la casse).</param>
public sealed record UserAccountFilter(
    Guid? AgencyId = null,
    RoleTemplate? Role = null,
    AccountStatus? Status = null,
    string? Search = null);
