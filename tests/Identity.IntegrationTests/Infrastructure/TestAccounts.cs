using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Infrastructure;

public sealed record TestAccount(Guid Id, string Email, string? Phone, string Password);

/// <summary>Crée et modifie des comptes directement par les services de l'API (données fictives).</summary>
public static class TestAccounts
{
    public const string DefaultPassword = "MotDePasse1";

    public static async Task<TestAccount> CreateAsync(
        HawalaApiFactory factory,
        RoleTemplate role = RoleTemplate.Agent,
        bool mustChangePassword = false,
        bool withPhone = false)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var email = Email.Create($"agent-{Guid.NewGuid():N}@example.com").Value;
        var phone = withPhone
            ? PhoneNumber.Create($"+2223{Random.Shared.Next(1_000_000, 9_999_999)}").Value
            : null;

        var account = UserAccount.CreateWithTemporaryPassword(
            email, phone, "Agent Test", role, DateTimeOffset.UtcNow).Value;

        if (!mustChangePassword)
        {
            account.PasswordChanged();
        }

        var created = await services.GetRequiredService<ICredentialStore>()
            .CreateAsync(account.Id, DefaultPassword, CancellationToken.None);
        Assert.True(created.IsSuccess);

        services.GetRequiredService<IUserAccountRepository>().Add(account);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        return new TestAccount(account.Id, email.Value, phone?.Value, DefaultPassword);
    }

    /// <summary>Bloque le compte comme le fera le futur endpoint du Superviseur.</summary>
    public static async Task BlockAsync(HawalaApiFactory factory, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var account = await services.GetRequiredService<IUserAccountRepository>()
            .GetByIdAsync(accountId, CancellationToken.None)
            ?? throw new InvalidOperationException("Compte de test introuvable.");

        Assert.True(account.Block(activeSupervisorCount: 2).IsSuccess);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}