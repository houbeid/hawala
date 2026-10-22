using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Transfers.Application.Features.Transfers.GetTransferSummary;

namespace Transfers.Application.Features.Transfers.GetTransferDashboard;

/// <summary>
/// Tableau de bord du Superviseur (décisions du 21 octobre 2026) : quatre cartes par caisse, dans sa devise ;
/// période sur la date d'opération, aujourd'hui par défaut.
/// </summary>
public sealed record GetTransferDashboardQuery(DateOnly? From = null, DateOnly? To = null) : IQuery<TransferDashboard>;

/// <summary>
/// Cartes d'une caisse, sur les transferts qu'elle a envoyés (annulés exclus), dans sa devise : envoyé = livré + non livré.
/// </summary>
/// <param name="Sent">« المبلغ المرسل » : payé par les clients à cette caisse, avec le nombre de transferts.</param>
/// <param name="NotDelivered">« الحوالات غير المسلمة » : ceux de ces transferts pas encore livrés par l'autre agence.</param>
/// <param name="Delivered">« المبلغ المسلم » : ceux déjà livrés.</param>
/// <param name="DeliveryRatePercent">« نسبة التسليم » : livrés / envoyés × 100, en nombre de transferts (pleine précision) ; null sans envoi.</param>
public sealed record CaisseDashboard(
    Guid AgencyId,
    Currency Currency,
    SummaryAmount Sent,
    SummaryAmount NotDelivered,
    SummaryAmount Delivered,
    decimal? DeliveryRatePercent);

public sealed record TransferDashboard(DateOnly From, DateOnly To, IReadOnlyList<CaisseDashboard> Caisses);
