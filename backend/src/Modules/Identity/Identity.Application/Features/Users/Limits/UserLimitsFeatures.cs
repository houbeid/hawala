using System.Globalization;
using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Organization.Contracts;

namespace Identity.Application.Features.Users.Limits;

/// <summary>Plafonds d'un compte, dans la devise de son agence ; null = pas de limite.</summary>
/// <param name="Currency">MRO à Nouakchott, XOF à Dakar ; null pour un Superviseur (jamais de plafond).</param>
public sealed record UserLimits(Guid UserId, string? Currency, decimal? PerTransfer, decimal? PerDay, decimal? CreditPerDay);

/// <param name="ActorId">Compte connecté, lu dans le JWT : Superviseur, ou compte de la même agence avec users.manage.</param>
public sealed record GetUserLimitsQuery(Guid ActorId, Guid UserId) : IQuery<UserLimits>;

/// <summary>État complet des trois plafonds (absent ou null = pas de limite).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : doit être un Superviseur.</param>
public sealed record SetUserLimitsCommand(
    Guid ActorId,
    Guid UserId,
    decimal? PerTransfer,
    decimal? PerDay,
    decimal? CreditPerDay) : ICommand<UserLimits>;

/// <summary>
/// Règles validées le 22 octobre 2026 : seul un Superviseur fixe les plafonds ; jamais de plafond pour un Superviseur ;
/// effet immédiat (relus à chaque transfert) ; tracé dans l'audit, même transaction.
/// </summary>
public sealed class UserLimitsHandlers(
    IUserAccountRepository accounts,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : IQueryHandler<GetUserLimitsQuery, UserLimits>,
      ICommandHandler<SetUserLimitsCommand, UserLimits>
{
    public const string AuditAction = "Identity.Account.LimitsChanged";

    public async Task<Result<UserLimits>> Handle(GetUserLimitsQuery query, CancellationToken cancellationToken)
    {
        var manager = await AccountManager.LoadAsync(accounts, query.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(query.UserId, cancellationToken);
        if (account is null || !manager.CanManage(account))
        {
            return IdentityErrors.Account.NotFound;
        }

        var currency = account.AgencyId is { } agencyId
            ? (await agencies.GetByIdAsync(agencyId, cancellationToken))?.Currency.ToString()
            : null;
        return ToView(account, currency);
    }

    public async Task<Result<UserLimits>> Handle(SetUserLimitsCommand command, CancellationToken cancellationToken)
    {
        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        if (!manager.IsSupervisor)
        {
            return IdentityErrors.Account.SupervisorOnly;
        }

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        if (account.Role == RoleTemplate.Supervisor)
        {
            return IdentityErrors.Limits.SupervisorHasNone;
        }

        if (account.AgencyId is not { } agencyId)
        {
            return IdentityErrors.Limits.AgencyRequired;
        }

        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        if (agency is null)
        {
            return IdentityErrors.Limits.AgencyRequired;
        }

        var before = account.Limits;
        var changed = account.SetLimits(
            new AccountLimits(command.PerTransfer, command.PerDay, command.CreditPerDay), agency.Currency);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (!changed.Value)
        {
            return ToView(account, agency.Currency.ToString());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var after = account.Limits;
        var changes = new[]
            {
                ("perTransfer", before.PerTransfer, after.PerTransfer),
                ("perDay", before.PerDay, after.PerDay),
                ("creditPerDay", before.CreditPerDay, after.CreditPerDay),
            }
            .Where(c => c.Item2 != c.Item3)
            .Select(c => new AuditChange(c.Item1, Text(c.Item2), Text(c.Item3)))
            .ToList();

        var recorded = await audit.RecordAsync(
            new AuditRecord(AuditAction, "UserAccount", account.Id, command.ActorId, agencyId, Changes: changes),
            cancellationToken);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        return ToView(account, agency.Currency.ToString());
    }

    private static UserLimits ToView(UserAccount account, string? currency) =>
        new(account.Id, currency, account.LimitPerTransfer, account.LimitPerDay, account.CreditLimitPerDay);

    // Sans zéros inutiles : un plafond relu en base (5000.00) et un plafond reçu (5000) s'écrivent pareil.
    private static string? Text(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);
}
