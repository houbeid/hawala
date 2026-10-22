using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.SetCreditLimit;

/// <summary>Le Superviseur fixe le plafond de crédit d'un tiers dans une agence (permission parties.manage).</summary>
/// <param name="CreditLimit">Dans la devise de l'agence ; positif ou nul (0 = aucun crédit) ; null = pas de plafond.</param>
/// <param name="ActorId">Compte connecté, lu dans le JWT (auteur de la ligne d'audit).</param>
public sealed record SetCreditLimitCommand(Guid ActorId, Guid PartyId, Guid AgencyId, decimal? CreditLimit) : ICommand;
