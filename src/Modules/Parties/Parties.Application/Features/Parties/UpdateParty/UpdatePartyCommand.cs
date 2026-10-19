using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.UpdateParty;

/// <summary>
/// Le Superviseur corrige la fiche d'un tiers (permission parties.manage). C'est l'état complet de la fiche :
/// un téléphone, un NNI, une adresse ou une note absents sont retirés.
/// Le solde initial et le plafond de crédit ont leurs propres actions.
/// </summary>
/// <param name="Type">Client, Supplier, Staff ou Other.</param>
/// <param name="InBalanceSheet">Obligatoire : un oubli ne doit pas sortir le tiers du bilan en silence.</param>
public sealed record UpdatePartyCommand(
    Guid PartyId,
    string? Name,
    string? Type,
    string? Phone,
    string? Nni,
    string? Address,
    string? Note,
    bool? InBalanceSheet) : ICommand;
