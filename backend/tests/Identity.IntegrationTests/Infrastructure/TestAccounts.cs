using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;
using Organization.Contracts;

namespace Identity.IntegrationTests.Infrastructure;

public sealed record TestAccount(Guid Id, string Email, string? Phone, string Password, Guid? AgencyId);

/// <summary>Crée et modifie des comptes directement par les services de l'API (données fictives).</summary>
public static class TestAccounts
{
    public const string DefaultPassword = "MotDePasse1";

    /// <param name="agencyCode">Agence d'un agent (NKC par défaut). Ignorée pour un Superviseur, qui n'en a pas.</param>
    public static async Task<TestAccount> CreateAsync(
        HawalaApiFactory factory,
        RoleTemplate role = RoleTemplate.Agent,
        bool mustChangePassword = false,
        bool withPhone = false,
        string agencyCode = "NKC")
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var email = Email.Create($"agent-{Guid.NewGuid():N}@example.com").Value;
        var phone = withPhone
            ? PhoneNumber.Create($"+2223{Random.Shared.Next(1_000_000, 9_999_999)}").Value
            : null;

        Guid? agencyId = role == RoleTemplate.Supervisor
            ? null
            : await GetAgencyIdAsync(services, agencyCode);

        var account = UserAccount.CreateWithTemporaryPassword(
            email, phone, "Agent Test", role, agencyId, DateTimeOffset.UtcNow).Value;

        if (!mustChangePassword)
        {
            account.PasswordChanged();
        }

        var created = await services.GetRequiredService<ICredentialStore>()
            .CreateAsync(account.Id, DefaultPassword, CancellationToken.None);
        Assert.True(created.IsSuccess);

        services.GetRequiredService<IUserAccountRepository>().Add(account);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        return new TestAccount(account.Id, email.Value, phone?.Value, DefaultPassword, agencyId);
    }

    /// <summary>Bloque le compte comme le fera le futur endpoint du Superviseur.</summary>
    public static async Task BlockAsync(HawalaApiFactory factory, Guid accountId)
    {
        await ChangeAsync(factory, accountId, account => account.Block(activeSupervisorCount: 2).IsSuccess);
    }

    /// <summary>Débloque directement en base (sans nouveau mot de passe) : sert à remettre la base en état après un test.</summary>
    public static async Task UnblockAsync(HawalaApiFactory factory, Guid accountId)
    {
        await ChangeAsync(factory, accountId, account => account.Unblock().IsSuccess);
    }

    /// <summary>Superviseurs actifs en base, tous tests confondus (la base est partagée).</summary>
    public static async Task<IReadOnlyList<Guid>> GetActiveSupervisorIdsAsync(HawalaApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var supervisors = await scope.ServiceProvider.GetRequiredService<IUserAccountRepository>().ListAsync(
            new UserAccountFilter(Role: RoleTemplate.Supervisor, Status: AccountStatus.Active),
            CancellationToken.None);

        return [.. supervisors.Select(a => a.Id)];
    }

    /// <summary>Déplace un agent vers une autre agence, comme le fera le futur endpoint du Superviseur.</summary>
    public static async Task MoveToAgencyAsync(HawalaApiFactory factory, Guid accountId, string agencyCode)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var agencyId = await GetAgencyIdAsync(scope.ServiceProvider, agencyCode);

        await ChangeAsync(factory, accountId, account => account.ChangeAgency(agencyId).IsSuccess);
    }

    public static async Task<Guid> GetAgencyIdAsync(IServiceProvider services, string agencyCode)
    {
        var agencies = await services.GetRequiredService<IAgencyDirectory>().GetAllAsync(CancellationToken.None);
        return agencies.Single(a => a.Code == agencyCode).Id;
    }

    private static async Task ChangeAsync(HawalaApiFactory factory, Guid accountId, Func<UserAccount, bool> change)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var account = await services.GetRequiredService<IUserAccountRepository>()
            .GetByIdAsync(accountId, CancellationToken.None)
            ?? throw new InvalidOperationException("Compte de test introuvable.");

        Assert.True(change(account));
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}