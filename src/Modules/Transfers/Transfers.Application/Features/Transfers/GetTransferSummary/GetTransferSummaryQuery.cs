using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;

namespace Transfers.Application.Features.Transfers.GetTransferSummary;

/// <summary>
/// Cartes du panneau de l'écran des transferts. Superviseur (profit.view) : toutes les cartes, agence choisie.
/// Autres comptes (règle validée le 21 octobre 2026) : leur agence seulement, et seulement le total acheté (entrées),
/// le total vendu (sorties), les non livrés et le prix de vente.
/// Mêmes filtres que la liste : période sur la date d'opération (aujourd'hui par défaut) et recherche texte.
/// </summary>
public sealed record GetTransferSummaryQuery(
    Guid ActorId,
    Guid? AgencyId,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null) : IQuery<TransferSummary>;

/// <summary>Un total et le nombre de transferts qui le composent.</summary>
public sealed record SummaryAmount(decimal Amount, int Count);

/// <summary>
/// Les cartes (lues sur l'app actuelle le 21 octobre 2026) : deux groupes de transferts, chacun dans les deux devises.
/// Agence choisie A, autre agence B ; transferts annulés exclus partout.
/// </summary>
/// <param name="Incoming">« دخل · A » : payé par les clients à A pour les transferts envoyés par A (devise de A).</param>
/// <param name="DeliveredByOther">« المبلغ المسلم · B » : remis par B aux bénéficiaires de ces transferts, livrés seulement (devise de B) ; null sans profit.view.</param>
/// <param name="SentByOther">« المبلغ المرسل · B » : payé par les clients à B pour les transferts envoyés par B (devise de B) ; null sans profit.view.</param>
/// <param name="Outgoing">« صرف · A » : remis par A aux bénéficiaires de ces transferts, livrés seulement (devise de A).</param>
/// <param name="NotDelivered">« غير مسلمة » : ce que A doit encore payer, dans sa devise.</param>
/// <param name="ProfitMro">« الربح » : somme des bénéfices des transferts de la période, dans les deux sens (pleine précision) ; null sans profit.view.</param>
/// <param name="SaleRate">Prix de vente du dernier jour de la période ; null s'il n'est pas fixé.</param>
/// <param name="PurchaseRate">Prix d'achat du même jour ; null s'il n'est pas fixé ou sans profit.view.</param>
public sealed record TransferSummary(
    Guid AgencyId,
    Currency Currency,
    Guid OtherAgencyId,
    Currency OtherCurrency,
    DateOnly From,
    DateOnly To,
    SummaryAmount Incoming,
    SummaryAmount Outgoing,
    SummaryAmount? SentByOther,
    SummaryAmount? DeliveredByOther,
    SummaryAmount NotDelivered,
    decimal? ProfitMro,
    decimal? SaleRate,
    decimal? PurchaseRate);
