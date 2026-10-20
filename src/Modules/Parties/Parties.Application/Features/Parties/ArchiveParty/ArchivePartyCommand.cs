using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.ArchiveParty;

/// <summary>Le Superviseur archive un tiers (permission parties.manage) : seulement à solde nul dans les deux agences.</summary>
public sealed record ArchivePartyCommand(Guid PartyId) : ICommand;

/// <summary>Le Superviseur désarchive un tiers (permission parties.manage).</summary>
public sealed record UnarchivePartyCommand(Guid PartyId) : ICommand;
