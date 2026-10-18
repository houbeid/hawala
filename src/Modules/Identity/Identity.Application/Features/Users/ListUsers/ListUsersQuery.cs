using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.ListUsers;

/// <summary>Liste des comptes pour le Superviseur. Tous les filtres sont facultatifs.</summary>
/// <param name="Role">Supervisor, Agent, Cashier ou ReadOnly.</param>
/// <param name="Status">Active ou Blocked. Sans filtre, les comptes bloqués sont inclus.</param>
/// <param name="Search">Texte cherché dans le nom, l'email ou le téléphone.</param>
public sealed record ListUsersQuery(
    Guid? AgencyId = null,
    string? Role = null,
    string? Status = null,
    string? Search = null) : IQuery<IReadOnlyList<UserSummary>>;
