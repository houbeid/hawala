using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.ResumeTransfers;

/// <summary>« Vérifier » : le rapport, sans rien créer.</summary>
public sealed class CheckResumedTransfersQueryHandler(ResumedTransferValidator validator)
    : IQueryHandler<CheckResumedTransfersQuery, ResumedTransferReport>
{
    public async Task<Result<ResumedTransferReport>> Handle(CheckResumedTransfersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (report, _) = await validator.ValidateAsync(query.Rows, cancellationToken);
        return report;
    }
}

/// <summary>
/// « Importer » (transaction commune, tout ou rien ; décisions du 24 octobre 2026). Le fichier est revérifié. Chaque
/// transfert est créé « Non livré », historique « Repris », ancienne référence et date d'origine, sans paiement ; un code
/// de retrait est tiré au hasard et jeté (personne ne le connaît : nouveau code par l'agence d'envoi, ou « livrer sans
/// code » par le Superviseur). Une écriture d'ouverture par transfert, à la date de mise en service, dans l'agence de
/// destination : « Soldes d'ouverture » (débit) contre « Transferts à livrer » (crédit), du montant à livrer : la
/// livraison videra ensuite « Transferts à livrer » comme pour tout transfert. Les caisses ne sont pas touchées (l'argent
/// est déjà dans leur solde de départ). Clé d'idempotence « resume:{référence} » : un même transfert n'est jamais repris
/// deux fois.
/// </summary>
public sealed class ImportResumedTransfersCommandHandler(
    ResumedTransferValidator validator,
    ITransferRepository transfers,
    IWithdrawalCodeHasher codeHasher,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<ImportResumedTransfersCommand, ResumedTransferResult>
{
    public const string EntryType = "Transfers.Resumed";

    public async Task<Result<ResumedTransferResult>> Handle(ImportResumedTransfersCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (report, rows) = await validator.ValidateAsync(command.Rows, cancellationToken);
        if (report.Issues.Count > 0)
        {
            return Error.Validation("Transfers.Resume.Invalid", "Le fichier contient des erreurs : rien n'a été importé.") with
            {
                Details = new Dictionary<string, object?> { ["report"] = report },
            };
        }

        var now = clock.UtcNow;
        var openingDate = command.OperationDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        foreach (var row in rows)
        {
            var created = Transfer.Resume(
                row.Transfer, row.Reference, codeHasher.Hash(WithdrawalCodes.NewCode()), $"resume:{row.Reference}", command.ActorId, now);
            if (created.IsFailure)
            {
                return RowFailed(row.Row, created.Error);
            }

            var transfer = created.Value;
            var entry = await PostOpeningAsync(transfer, openingDate, command.ActorId, cancellationToken);
            if (entry.IsFailure)
            {
                return RowFailed(row.Row, entry.Error);
            }

            transfer.AttachCreationEntry(entry.Value);
            transfers.Add(transfer);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ResumedTransferResult(rows.Count);
    }

    private async Task<Result<Guid>> PostOpeningAsync(Transfer transfer, DateOnly date, Guid actorId, CancellationToken cancellationToken)
    {
        var opening = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OpeningBalances, transfer.DestinationAgencyId, transfer.DestinationCurrency, cancellationToken);
        if (opening.IsFailure)
        {
            return opening.Error;
        }

        var toDeliver = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.TransfersToDeliver, transfer.DestinationAgencyId, transfer.DestinationCurrency, cancellationToken);
        if (toDeliver.IsFailure)
        {
            return toDeliver.Error;
        }

        var amount = transfer.AmountToDeliver;
        return await ledger.PostAsync(
            new PostEntryRequest(
                date,
                EntryType,
                "Transfers",
                transfer.Id,
                actorId,
                $"Transfert repris de l'ancienne app {transfer.Reference}",
                [
                    new PostEntryLine(opening.Value, EntrySide.Debit, amount),
                    new PostEntryLine(toDeliver.Value, EntrySide.Credit, amount),
                ]),
            cancellationToken);
    }

    private static Error RowFailed(int row, Error cause) =>
        Error.Validation("Transfers.Resume.RowFailed", "Une ligne a été refusée : rien n'a été importé.") with
        {
            Details = new Dictionary<string, object?> { ["row"] = row, ["cause"] = cause.Code },
        };
}
