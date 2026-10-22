using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.ArchiveParty;

/// <summary>Le Superviseur archive un tiers (permission parties.manage) : seulement à solde nul dans les deux agences.</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT (auteur de la ligne d'audit).</param>
public sealed record ArchivePartyCommand(Guid ActorId, Guid PartyId) : ICommand;

/// <summary>Le Superviseur désarchive un tiers (permission parties.manage).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT (auteur de la ligne d'audit).</param>
public sealed record UnarchivePartyCommand(Guid ActorId, Guid PartyId) : ICommand;
