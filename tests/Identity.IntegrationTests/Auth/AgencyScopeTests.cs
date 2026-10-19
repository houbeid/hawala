using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.Infrastructure.Sessions;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Auth;

/// <summary>
/// Périmètre par agence (2e couche de sécurité) : rempli par la validation de session, une fois par requête.
/// Chaque scope DI joue le rôle d'une requête HTTP.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AgencyScopeTests(ApiFixture fixture)
{
    // TestAccounts crée les comptes au mot de passe déjà changé : SecurityVersion = 2.
    private const int CurrentSecurityVersion = 2;

    [Theory]
    [InlineData("NKC")]
    [InlineData("DKR")]
    public async Task Agent_sees_only_its_own_agency(string agencyCode)
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: agencyCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var other = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, agencyCode == "NKC" ? "DKR" : "NKC");

        var agencyScope = await ValidateAsync(scope, agent.Id, CurrentSecurityVersion);

        Assert.False(agencyScope.SeesAllAgencies);
        Assert.Equal(agent.AgencyId, agencyScope.AgencyId);
        Assert.True(agencyScope.CanSee(agent.AgencyId!.Value));
        Assert.False(agencyScope.CanSee(other));
    }

    [Fact]
    public async Task Supervisor_sees_both_agencies()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        await using var scope = factory.Services.CreateAsyncScope();

        var agencyScope = await ValidateAsync(scope, supervisor.Id, CurrentSecurityVersion);

        Assert.True(agencyScope.SeesAllAgencies);
        Assert.Null(agencyScope.AgencyId);
        Assert.True(agencyScope.CanSee(await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC")));
        Assert.True(agencyScope.CanSee(await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR")));
    }

    [Fact]
    public async Task Without_a_validated_session_no_agency_is_visible()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();

        // Jeton périmé (mauvaise SecurityVersion) : la session est refusée, le périmètre reste vide.
        var refused = await ValidateAsync(scope, agent.Id, CurrentSecurityVersion + 1);

        Assert.False(refused.SeesAllAgencies);
        Assert.Null(refused.AgencyId);
        Assert.False(refused.CanSee(agent.AgencyId!.Value));

        // Requête sans aucune validation (tâche de fond, appel anonyme) : même chose.
        await using var anonymous = factory.Services.CreateAsyncScope();
        var empty = anonymous.ServiceProvider.GetRequiredService<IAgencyScope>();
        Assert.False(empty.CanSee(agent.AgencyId!.Value));
    }

    [Fact]
    public async Task Moved_agent_gets_the_new_agency_in_its_next_validated_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        await TestAccounts.MoveToAgencyAsync(factory, agent.Id, "DKR");
        await using var scope = factory.Services.CreateAsyncScope();
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        // Le déplacement a augmenté la SecurityVersion : l'ancienne est refusée, la nouvelle voit Dakar.
        var agencyScope = await ValidateAsync(scope, agent.Id, CurrentSecurityVersion + 1);

        Assert.Equal(dakar, agencyScope.AgencyId);
    }

    private static async Task<IAgencyScope> ValidateAsync(AsyncServiceScope scope, Guid accountId, int securityVersion)
    {
        var validator = scope.ServiceProvider.GetRequiredService<SessionValidator>();
        await validator.IsSessionCurrentAsync(accountId, securityVersion, CancellationToken.None);
        return scope.ServiceProvider.GetRequiredService<IAgencyScope>();
    }
}
