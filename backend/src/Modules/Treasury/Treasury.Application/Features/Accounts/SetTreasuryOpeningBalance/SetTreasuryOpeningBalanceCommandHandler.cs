using System.Globalization;
using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.SetTreasuryOpeningBalance;

/// <summary>
/// Règles du 22 octobre 2026 : le solde de départ passe par une écriture d'ouverture DE LA DIFFÉRENCE, contre le compte
/// système « Soldes d'ouverture » de l'agence (rattaché au capital : ni gain ni perte), à la date choisie (jamais future,
/// refusée par le grand livre) ; jamais par un ajustement (« Écarts et ajustements » sert aux corrections). Le solde de
/// départ actuel n'est gardé qu'au grand livre : somme des écritures « Treasury.OpeningBalance » du compte. Même valeur :
/// rien n'est écrit. Ligne d'audit (avant / après) et alerte du contrôle de cohérence, comme pour un tiers.
/// Un compte désactivé n'a pas de solde de départ à changer. Hors périmètre : 404.
/// </summary>
public sealed class SetTreasuryOpeningBalanceCommandHandler(
    ITreasuryAccountRepository accounts,
    ILedger ledger,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<SetTreasuryOpeningBalanceCommand>
{
    public const string EntryType = "Treasury.OpeningBalance";
    public const string AuditAction = "Treasury.Account.OpeningBalanceChanged";

    public async Task<Result> Handle(SetTreasuryOpeningBalanceCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount is not { } amount)
        {
            return TreasuryErrors.Account.OpeningBalanceRequired;
        }

        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
        }

        if (!account.IsActive)
        {
            return TreasuryErrors.Account.Inactive;
        }

        // Dans la devise du compte : FCFA entier, MRO à 2 décimales au plus (refusé, jamais arrondi).
        var valid = Money.Create(amount, account.Currency);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var before = await ledger.SumEntriesAsync(account.LedgerAccountId, EntryType, cancellationToken);
        var difference = amount - before;
        if (difference == 0m)
        {
            return Result.Success();
        }

        var openingBalances = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OpeningBalances, account.AgencyId, account.Currency, cancellationToken);
        if (openingBalances.IsFailure)
        {
            return openingBalances.Error;
        }

        // Positif : il y a plus d'argent sur ce compte (débit) ; négatif : moins (crédit).
        var line = Money.Create(Math.Abs(difference), account.Currency).Value;
        var accountSide = difference > 0m ? EntrySide.Debit : EntrySide.Credit;
        var counterSide = difference > 0m ? EntrySide.Credit : EntrySide.Debit;
        var posted = await ledger.PostAsync(
            new PostEntryRequest(
                command.OperationDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                EntryType,
                "Treasury",
                account.Id,
                command.ActorId,
                $"Solde initial : {account.Name}",
                [
                    new PostEntryLine(account.LedgerAccountId, accountSide, line),
                    new PostEntryLine(openingBalances.Value, counterSide, line),
                ]),
            cancellationToken);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var recorded = await audit.RecordAsync(
            new AuditRecord(
                AuditAction,
                "TreasuryAccount",
                account.Id,
                command.ActorId,
                account.AgencyId,
                Changes: [new AuditChange("openingBalance", Text(before), Text(amount))]),
            cancellationToken);
        return recorded.IsFailure ? recorded.Error : Result.Success();
    }

    /// <summary>Montant invariant pour l'audit, comme les autres réglages (« 0.## »).</summary>
    private static string Text(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
