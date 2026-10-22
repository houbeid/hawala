using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;
using Parties.Domain.Scopes;

namespace Parties.Application.Features.Scopes;

/// <summary>« Limiter les tiers » d'un compte, tel que le Superviseur le voit.</summary>
/// <param name="Parties">Tiers autorisés (vide si le compte n'est pas limité), triés par nom.</param>
public sealed record AccountPartyScopeView(Guid AccountId, bool Limited, IReadOnlyList<AllowedPartyView> Parties);

public sealed record AllowedPartyView(Guid Id, string Name, bool IsArchived);

/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : doit être un Superviseur.</param>
public sealed record GetAccountPartyScopeQuery(Guid ActorId, Guid AccountId) : IQuery<AccountPartyScopeView>;

/// <summary>État complet : limité ou non, et la liste des tiers autorisés (ignorée si non limité).</summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : doit être un Superviseur.</param>
/// <param name="Limited">Obligatoire.</param>
public sealed record SetAccountPartyScopeCommand(
    Guid ActorId,
    Guid AccountId,
    bool? Limited,
    IReadOnlyList<Guid>? PartyIds) : ICommand<AccountPartyScopeView>;

/// <summary>
/// Règles validées le 21 octobre 2026 : seul un Superviseur fixe la liste ; un Superviseur n'est jamais limité ;
/// effet immédiat (la liste est relue à chaque requête) ; tracé dans l'audit, même transaction.
/// </summary>
public sealed class AccountPartyScopeHandlers(
    IAccountDirectory accounts,
    IPartyScopeRepository scopes,
    IPartyRepository parties,
    IUnitOfWork unitOfWork,
    IAuditLog audit,
    IClock clock)
    : IQueryHandler<GetAccountPartyScopeQuery, AccountPartyScopeView>,
      ICommandHandler<SetAccountPartyScopeCommand, AccountPartyScopeView>
{
    public const string AuditAction = "Parties.AccountScope.Changed";

    public async Task<Result<AccountPartyScopeView>> Handle(GetAccountPartyScopeQuery query, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(query.ActorId, query.AccountId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var scope = await scopes.GetAsync(query.AccountId, cancellationToken);
        return await ToViewAsync(query.AccountId, scope, cancellationToken);
    }

    public async Task<Result<AccountPartyScopeView>> Handle(SetAccountPartyScopeCommand command, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(command.ActorId, command.AccountId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        if (command.Limited is not { } limited)
        {
            return PartiesErrors.Scope.LimitedRequired;
        }

        List<Guid> partyIds = limited ? [.. (command.PartyIds ?? []).Distinct()] : [];
        var existing = await parties.GetByIdsIgnoringScopeAsync(partyIds, cancellationToken);
        if (existing.Count != partyIds.Count)
        {
            return PartiesErrors.Scope.PartyNotFound;
        }

        var now = clock.UtcNow;
        var scope = await scopes.GetAsync(command.AccountId, cancellationToken);
        if (scope is null)
        {
            scope = AccountPartyScope.Create(command.AccountId, now);
            scopes.Add(scope);
        }

        var wasLimited = scope.IsLimited;
        var before = scope.AllowedParties.Select(p => p.PartyId).ToHashSet();
        if (!scope.Set(limited, partyIds, now))
        {
            return await ToViewAsync(command.AccountId, scope, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var after = scope.AllowedParties.Select(p => p.PartyId).ToHashSet();
        var changes = new List<AuditChange>();
        if (wasLimited != limited)
        {
            changes.Add(new AuditChange("limited", Text(wasLimited), Text(limited)));
        }

        changes.AddRange(before.Union(after)
            .Where(id => before.Contains(id) != after.Contains(id))
            .Order()
            .Select(id => new AuditChange($"party:{id}", Allowed(before, id), Allowed(after, id))));

        var recorded = await audit.RecordAsync(
            new AuditRecord(AuditAction, "UserAccount", command.AccountId, command.ActorId, target.Value.AgencyId, Changes: changes),
            cancellationToken);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        return await ToViewAsync(command.AccountId, scope, cancellationToken);
    }

    /// <summary>Auteur Superviseur, compte cible existant et non Superviseur.</summary>
    private async Task<Result<AccountInfo>> CheckAsync(Guid actorId, Guid accountId, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(actorId, cancellationToken) is not { IsSupervisor: true })
        {
            return PartiesErrors.Scope.SupervisorOnly;
        }

        var target = await accounts.FindAsync(accountId, cancellationToken);
        if (target is null)
        {
            return PartiesErrors.Scope.AccountNotFound;
        }

        if (target.IsSupervisor)
        {
            return PartiesErrors.Scope.SupervisorSeesAll;
        }

        return target;
    }

    private async Task<AccountPartyScopeView> ToViewAsync(Guid accountId, AccountPartyScope? scope, CancellationToken cancellationToken)
    {
        if (scope is not { IsLimited: true })
        {
            return new AccountPartyScopeView(accountId, false, []);
        }

        var found = await parties.GetByIdsIgnoringScopeAsync(
            [.. scope.AllowedParties.Select(p => p.PartyId)], cancellationToken);

        return new AccountPartyScopeView(
            accountId,
            true,
            [.. found.OrderBy(p => p.Name, StringComparer.CurrentCulture).ThenBy(p => p.Id)
                .Select(p => new AllowedPartyView(p.Id, p.Name, p.IsArchived))]);
    }

    private static string Text(bool value) => value ? "true" : "false";

    private static string Allowed(HashSet<Guid> allowed, Guid id) => allowed.Contains(id) ? "allowed" : "not_allowed";
}
