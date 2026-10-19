using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.CreateParty;

/// <summary>
/// Création d'un tiers (permission parties.create : Superviseur et agents, règle validée).
/// Pas de solde initial ici : il passe toujours par l'action « solde initial par agence ».
/// </summary>
/// <param name="Type">Client, Supplier, Staff ou Other.</param>
public sealed record CreatePartyCommand(
    string? Name,
    string? Type,
    string? Phone,
    string? Nni,
    string? Address,
    string? Note) : ICommand<Guid>;
