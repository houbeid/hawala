using Hawala.SharedKernel.Messaging;

namespace Organization.Application.Agencies.ChangeAgencyColor;

/// <summary>Le Superviseur change la couleur d'une agence (permission agencies.manage).</summary>
/// <param name="Color">Format #RRGGBB, minuscules acceptées (enregistrée en majuscules).</param>
/// <param name="ActorId">Compte connecté, lu dans le JWT (auteur de la ligne d'audit).</param>
public sealed record ChangeAgencyColorCommand(Guid ActorId, Guid AgencyId, string? Color) : ICommand;
