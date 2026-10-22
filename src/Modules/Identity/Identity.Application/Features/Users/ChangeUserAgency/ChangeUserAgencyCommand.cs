using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.ChangeUserAgency;

/// <summary>Le Superviseur déplace un agent vers une autre agence (ex. un employé qui part travailler à Dakar).</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : doit être un Superviseur.</param>
public sealed record ChangeUserAgencyCommand(Guid ActorId, Guid UserId, Guid? AgencyId) : ICommand;
