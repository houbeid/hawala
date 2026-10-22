namespace Transfers.Application.Abstractions;

/// <summary>
/// Référence lisible d'un transfert, tirée d'un compteur par agence et par jour, dans la transaction de la commande
/// (un transfert annulé par une erreur ne consomme pas de numéro).
/// </summary>
public interface ITransferReferenceGenerator
{
    Task<string> NextAsync(Guid agencyId, string agencyCode, DateOnly day, CancellationToken cancellationToken);
}
