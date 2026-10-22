using Identity.Contracts;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Domain.Tests.Roles;

/// <summary>Ajouts / retraits de permissions par compte (phase 6, règles validées le 21 octobre 2026).</summary>
public class AccountPermissionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Dakar = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

    private static UserAccount Account(RoleTemplate role = RoleTemplate.Agent) =>
        UserAccount.CreateWithTemporaryPassword(
            Email.Create($"compte-{Guid.NewGuid():N}@example.com").Value,
            phone: null,
            "Compte Test",
            role,
            role == RoleTemplate.Supervisor ? null : Nouakchott,
            Now).Value;

    private static string[] AgentTemplateWith(params string[] extra) =>
        [.. RolePermissions.For(RoleTemplate.Agent).Concat(extra)];

    [Fact]
    public void A_new_account_has_exactly_its_role_template()
    {
        var account = Account();

        Assert.Empty(account.PermissionOverrides);
        Assert.Equal(RolePermissions.For(RoleTemplate.Agent).Order(), account.EffectivePermissions.Order());
    }

    [Fact]
    public void Any_permission_can_be_given_to_an_agent()
    {
        var account = Account();
        var version = account.SecurityVersion;

        var changed = account.SetPermissions(AgentTemplateWith(Permissions.TransfersCancel, Permissions.UsersManage));

        Assert.True(changed.Value);
        Assert.Contains(Permissions.TransfersCancel, account.EffectivePermissions);
        Assert.Contains(Permissions.UsersManage, account.EffectivePermissions);
        Assert.Equal(
            [
                new PermissionOverride(Permissions.TransfersCancel, PermissionOverrideKind.Granted),
                new PermissionOverride(Permissions.UsersManage, PermissionOverrideKind.Granted),
            ],
            account.PermissionOverrides);
        Assert.Equal(version + 1, account.SecurityVersion);
    }

    [Fact]
    public void A_template_permission_can_be_taken_away()
    {
        var account = Account();
        var desired = RolePermissions.For(RoleTemplate.Agent).Where(p => p != Permissions.TransfersCredit).ToList();

        account.SetPermissions(desired);

        Assert.DoesNotContain(Permissions.TransfersCredit, account.EffectivePermissions);
        Assert.Equal(
            [new PermissionOverride(Permissions.TransfersCredit, PermissionOverrideKind.Revoked)],
            account.PermissionOverrides);
    }

    [Fact]
    public void An_empty_list_removes_everything()
    {
        var account = Account(RoleTemplate.Cashier);

        account.SetPermissions([]);

        Assert.Empty(account.EffectivePermissions);
        Assert.All(account.PermissionOverrides, o => Assert.Equal(PermissionOverrideKind.Revoked, o.Kind));
    }

    [Fact]
    public void Same_permissions_change_nothing_and_keep_the_sessions()
    {
        var account = Account();
        account.SetPermissions(AgentTemplateWith(Permissions.ProfitView));
        var version = account.SecurityVersion;
        var overrides = account.PermissionOverrides.ToList();

        var changed = account.SetPermissions(AgentTemplateWith(Permissions.ProfitView, Permissions.ProfitView));

        Assert.False(changed.Value);
        Assert.Equal(version, account.SecurityVersion);

        // Les mêmes objets restent en place (EF ne les supprime pas pour les recréer).
        Assert.Same(overrides[0], account.PermissionOverrides[0]);
    }

    [Fact]
    public void Back_to_the_template_leaves_no_override()
    {
        var account = Account();
        account.SetPermissions(AgentTemplateWith(Permissions.ProfitView));

        account.SetPermissions(RolePermissions.For(RoleTemplate.Agent).ToList());

        Assert.Empty(account.PermissionOverrides);
    }

    [Fact]
    public void Unchanged_overrides_stay_when_another_one_changes()
    {
        var account = Account();
        account.SetPermissions(AgentTemplateWith(Permissions.ProfitView));
        var kept = account.PermissionOverrides.Single();

        account.SetPermissions(AgentTemplateWith(Permissions.ProfitView, Permissions.BalanceView));

        Assert.Equal(2, account.PermissionOverrides.Count);
        Assert.Contains(account.PermissionOverrides, o => ReferenceEquals(o, kept));
    }

    [Theory]
    [InlineData("transfers.everything")]
    [InlineData("TRANSFERS.CREATE")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_permission_is_refused(string? permission)
    {
        var account = Account();

        var result = account.SetPermissions([.. AgentTemplateWith(), permission]);

        Assert.Equal("Identity.Permissions.Unknown", result.Error.Code);
        Assert.Empty(account.PermissionOverrides);
    }

    [Fact]
    public void A_supervisor_always_keeps_everything()
    {
        var supervisor = Account(RoleTemplate.Supervisor);

        var result = supervisor.SetPermissions([Permissions.TransfersCreate]);

        Assert.Equal("Identity.Permissions.SupervisorHasAll", result.Error.Code);
        Assert.Equal(Permissions.All.Order(), supervisor.EffectivePermissions.Order());
    }

    [Fact]
    public void Changing_the_role_starts_again_from_the_new_template()
    {
        var account = Account();
        account.SetPermissions([Permissions.ProfitView]);

        account.ChangeRole(RoleTemplate.Cashier, Dakar, activeSupervisorCount: 2);

        Assert.Empty(account.PermissionOverrides);
        Assert.Equal(RolePermissions.For(RoleTemplate.Cashier).Order(), account.EffectivePermissions.Order());
    }

    [Fact]
    public void Moving_to_another_agency_keeps_the_overrides()
    {
        var account = Account();
        account.SetPermissions(AgentTemplateWith(Permissions.ProfitView));

        account.ChangeAgency(Dakar);

        Assert.Contains(Permissions.ProfitView, account.EffectivePermissions);
    }

    [Fact]
    public void Effective_ignores_overrides_of_a_supervisor()
    {
        var effective = AccountPermissions.Effective(
            RoleTemplate.Supervisor,
            [new PermissionOverride(Permissions.UsersManage, PermissionOverrideKind.Revoked)]);

        Assert.Contains(Permissions.UsersManage, effective);
    }

    [Fact]
    public void Effective_ignores_a_code_that_is_not_in_the_catalog()
    {
        var effective = AccountPermissions.Effective(
            RoleTemplate.ReadOnly,
            [new PermissionOverride("old.permission", PermissionOverrideKind.Granted)]);

        Assert.DoesNotContain("old.permission", effective);
    }
}
