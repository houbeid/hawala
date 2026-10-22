using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Organization.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;
using Treasury.Domain.Accounts;

namespace Treasury.Application.Features.Accounts.CreateTreasuryAccount;

/// <summary>
/// Crée le compte de trésorerie ET son compte au grand livre, dans une seule transaction (décorateur commun,
/// voir Infrastructure) : si l'un échoue, aucun des deux n'existe.
/// La devise vient de l'agence (module Organisation), jamais de la saisie.
/// POINT OUVERT : l'audit « qui a créé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class CreateTreasuryAccountCommandHandler(
    ITreasuryAccountRepository accounts,
    IAgencyDirectory agencies,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CreateTreasuryAccountCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTreasuryAccountCommand command, CancellationToken cancellationToken)
    {
        if (!EnumNames.TryParse<TreasuryAccountType>(command.Type, out var type))
        {
            return TreasuryErrors.Account.TypeInvalid;
        }

        if (command.AgencyId is not { } agencyId || agencyId == Guid.Empty)
        {
            return TreasuryErrors.Account.AgencyRequired;
        }

        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        if (agency is null)
        {
            return TreasuryErrors.Account.AgencyNotFound;
        }

        var account = TreasuryAccount.Create(command.Name, type, agencyId, agency.Currency, clock.UtcNow);
        if (account.IsFailure)
        {
            return account.Error;
        }

        // Message clair ; l'index unique en base reste le vrai garde-fou en cas de course.
        if (await accounts.NameExistsAsync(agencyId, account.Value.Name, excludingAccountId: null, cancellationToken))
        {
            return TreasuryErrors.Account.NameTaken;
        }

        var ledgerAccount = await ledger.OpenAccountAsync(
            LedgerAccountKind.Treasury,
            account.Value.Id,
            account.Value.Name,
            account.Value.Currency,
            agencyId,
            cancellationToken);
        if (ledgerAccount.IsFailure)
        {
            return ledgerAccount.Error;
        }

        account.Value.AttachLedgerAccount(ledgerAccount.Value);
        accounts.Add(account.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return account.Value.Id;
    }
}
