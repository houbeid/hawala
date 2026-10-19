using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.SetOpeningBalance;

/// <summary>
/// Le Superviseur fixe le solde initial d'un tiers dans une agence (permission parties.manage), à tout moment.
/// </summary>
/// <param name="ActorId">Superviseur connecté, lu dans le JWT : auteur de l'écriture.</param>
/// <param name="Amount">Dans la devise de l'agence ; positif = rouge (il nous doit), négatif = vert (on lui doit).</param>
/// <param name="OperationDate">Date de l'écriture d'ouverture (ex. jour de mise en service) ; aujourd'hui par défaut.</param>
public sealed record SetOpeningBalanceCommand(
    Guid ActorId,
    Guid PartyId,
    Guid AgencyId,
    decimal? Amount,
    DateOnly? OperationDate) : ICommand;
