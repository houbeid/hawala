using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.ListTransfers;

/// <summary>
/// Liste des transferts (règles validées le 20 octobre 2026) : période sur la date d'opération, aujourd'hui par défaut ;
/// du plus récent au plus ancien ; 50 par page avec le nombre total.
/// </summary>
/// <param name="From">Premier jour inclus ; aujourd'hui par défaut.</param>
/// <param name="To">Dernier jour inclus ; aujourd'hui par défaut.</param>
/// <param name="Status">NotDelivered, Delivered ou Cancelled.</param>
/// <param name="Direction">Sent ou Received, vu depuis l'agence de l'agent (ou celle choisie par le Superviseur).</param>
/// <param name="AgencyId">Superviseur : l'agence dont on veut les transferts. Agent : la sienne seulement.</param>
/// <param name="Search">Référence, expéditeur, bénéficiaire ou note (sans tenir compte de la casse).</param>
/// <param name="Page">Numéro de page, à partir de 1.</param>
public sealed record ListTransfersQuery(
    Guid ActorId,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Status = null,
    string? Direction = null,
    Guid? AgencyId = null,
    string? Search = null,
    int? Page = null) : IQuery<TransferList>;
