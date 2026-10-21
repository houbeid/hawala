using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.EditTransferTexts;

/// <summary>
/// Modifier l'expéditeur, le bénéficiaire et la note d'un transfert non livré : agence d'envoi (transfers.create) ou Superviseur.
/// État complet des trois champs (une note vide la retire) ; motif facultatif.
/// </summary>
/// <param name="Version">Version lue avec la fiche : obligatoire ; si elle a changé, conflit (409).</param>
public sealed record EditTransferTextsCommand(
    Guid ActorId,
    Guid TransferId,
    int? Version,
    string? SenderInfo,
    string? BeneficiaryInfo,
    string? Note,
    string? Reason) : ICommand;
