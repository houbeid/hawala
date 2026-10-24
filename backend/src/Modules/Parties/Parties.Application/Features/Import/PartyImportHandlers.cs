using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Parties.Application.Features.Parties.CreateParty;
using Parties.Application.Features.Parties.SetCreditLimit;
using Parties.Application.Features.Parties.SetOpeningBalance;
using Parties.Application.Features.Parties.UpdateParty;

namespace Parties.Application.Features.Import;

/// <summary>« Vérifier » : le rapport, sans rien créer.</summary>
public sealed class CheckPartyImportQueryHandler(PartyImportValidator validator)
    : IQueryHandler<CheckPartyImportQuery, PartyImportReport>
{
    public async Task<Result<PartyImportReport>> Handle(CheckPartyImportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (report, _) = await validator.ValidateAsync(query.Rows, cancellationToken);
        return report;
    }
}

/// <summary>
/// « Importer » : tout ou rien, dans la transaction commune (enregistrée avec AddSharedTransactional). Le fichier est
/// revérifié ; au moindre problème, rien n'est créé et le rapport revient avec l'erreur. Chaque tiers passe par les mêmes
/// étapes qu'à l'écran (mêmes handlers, sans leur propre transaction) : création (comptes au grand livre dans chaque
/// agence, audit), fiche « hors bilan » si demandé, solde initial par écriture d'ouverture à la date de mise en service,
/// plafond de crédit. Une étape refusée annule tout l'import.
/// </summary>
public sealed class ImportPartiesCommandHandler(
    PartyImportValidator validator,
    CreatePartyCommandHandler create,
    UpdatePartyCommandHandler update,
    SetOpeningBalanceCommandHandler openingBalance,
    SetCreditLimitCommandHandler creditLimit)
    : ICommandHandler<ImportPartiesCommand, PartyImportResult>
{
    public async Task<Result<PartyImportResult>> Handle(ImportPartiesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (report, rows) = await validator.ValidateAsync(command.Rows, cancellationToken);
        if (report.Issues.Count > 0)
        {
            return PartyImportErrors.Invalid(report);
        }

        foreach (var row in rows)
        {
            var created = await create.Handle(
                new CreatePartyCommand(row.Name, row.Type.ToString(), row.Phone, row.Nni, row.Address, row.Note), cancellationToken);
            if (created.IsFailure)
            {
                return PartyImportErrors.RowFailed(row.Row, created.Error);
            }

            var partyId = created.Value;
            if (!row.InBalanceSheet)
            {
                var updated = await update.Handle(
                    new UpdatePartyCommand(command.ActorId, partyId, row.Name, row.Type.ToString(), row.Phone, row.Nni, row.Address, row.Note, false),
                    cancellationToken);
                if (updated.IsFailure)
                {
                    return PartyImportErrors.RowFailed(row.Row, updated.Error);
                }
            }

            foreach (var agency in row.Agencies)
            {
                if (agency.Opening != 0m)
                {
                    var opened = await openingBalance.Handle(
                        new SetOpeningBalanceCommand(command.ActorId, partyId, agency.AgencyId, agency.Opening, command.OperationDate),
                        cancellationToken);
                    if (opened.IsFailure)
                    {
                        return PartyImportErrors.RowFailed(row.Row, opened.Error);
                    }
                }

                if (agency.CreditLimit is { } limit)
                {
                    var limited = await creditLimit.Handle(
                        new SetCreditLimitCommand(command.ActorId, partyId, agency.AgencyId, limit), cancellationToken);
                    if (limited.IsFailure)
                    {
                        return PartyImportErrors.RowFailed(row.Row, limited.Error);
                    }
                }
            }
        }

        return new PartyImportResult(rows.Count);
    }
}

/// <summary>Refus de l'import : le rapport (ou la ligne et la raison) part avec l'erreur, dans le ProblemDetails.</summary>
public static class PartyImportErrors
{
    public static Error Invalid(PartyImportReport report) =>
        Error.Validation("Parties.Import.Invalid", "Le fichier contient des erreurs : rien n'a été importé.") with
        {
            Details = new Dictionary<string, object?> { ["report"] = report },
        };

    public static Error RowFailed(int row, Error cause) =>
        Error.Validation("Parties.Import.RowFailed", "Une ligne a été refusée : rien n'a été importé.") with
        {
            Details = new Dictionary<string, object?> { ["row"] = row, ["cause"] = cause.Code },
        };
}
