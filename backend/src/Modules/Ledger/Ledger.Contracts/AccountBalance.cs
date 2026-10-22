using Hawala.SharedKernel.Money;

namespace Ledger.Contracts;

/// <summary>
/// Solde d'un compte = Σ débits − Σ crédits, dans la devise du compte.
/// Caisse : positif = argent en caisse. Tiers : positif = rouge (il nous doit), négatif = vert (on lui doit).
/// </summary>
public sealed record AccountBalance(Guid AccountId, Money Balance);
