using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.GetParty;

/// <summary>Fiche d'un tiers (cible de l'en-tête Location renvoyé à la création).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : décide si les soldes sont montrés.</param>
/// <param name="AgencyId">Agence dont on veut le solde ; sans valeur, toutes les agences visibles.</param>
public sealed record GetPartyQuery(Guid ActorId, Guid PartyId, Guid? AgencyId = null) : IQuery<PartySummary>;
