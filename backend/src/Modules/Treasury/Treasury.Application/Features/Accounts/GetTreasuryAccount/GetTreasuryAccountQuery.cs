using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.GetTreasuryAccount;

/// <summary>Fiche d'un compte de trésorerie (cible de l'en-tête Location renvoyé à la création).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : décide si le solde est montré.</param>
public sealed record GetTreasuryAccountQuery(Guid ActorId, Guid AccountId) : IQuery<TreasuryAccountSummary>;
