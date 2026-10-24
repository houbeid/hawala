using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.SetTreasuryOpeningBalance;

/// <summary>
/// Le Superviseur fixe le solde de départ d'un compte de trésorerie (permission treasury.manage ; décision du 22 octobre
/// 2026, phase 8) : à la mise en service, le solde lu dans l'ancienne app ou compté en caisse.
/// </summary>
/// <param name="ActorId">Superviseur connecté, lu dans le JWT : auteur de l'écriture.</param>
/// <param name="Amount">Solde de départ voulu, dans la devise du compte (FCFA entier, MRO à 2 décimales au plus).</param>
/// <param name="OperationDate">Date de l'écriture d'ouverture (jour de mise en service) ; aujourd'hui par défaut.</param>
public sealed record SetTreasuryOpeningBalanceCommand(
    Guid ActorId,
    Guid AccountId,
    decimal? Amount,
    DateOnly? OperationDate) : ICommand;
