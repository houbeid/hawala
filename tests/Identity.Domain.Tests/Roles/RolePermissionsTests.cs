using Identity.Contracts;
using Identity.Domain.Roles;

namespace Identity.Domain.Tests.Roles;

public class RolePermissionsTests
{
    [Fact]
    public void Supervisor_has_every_permission_of_the_catalog()
    {
        var granted = RolePermissions.For(RoleTemplate.Supervisor);

        Assert.All(Permissions.All, permission => Assert.Contains(permission, granted));
    }

    [Theory]
    [InlineData(RoleTemplate.Agent)]
    [InlineData(RoleTemplate.Cashier)]
    [InlineData(RoleTemplate.ReadOnly)]
    public void Agent_roles_cannot_manage_accounts_or_agencies(RoleTemplate role)
    {
        var granted = RolePermissions.For(role);

        Assert.DoesNotContain(Permissions.UsersManage, granted);
        Assert.DoesNotContain(Permissions.AgenciesManage, granted);

        // Règle validée : seul le Superviseur annule un transfert.
        Assert.DoesNotContain(Permissions.TransfersCancel, granted);
        Assert.DoesNotContain(Permissions.TransfersManageCode, granted);
        Assert.DoesNotContain(Permissions.TransfersEditRate, granted);
    }

    // Droits par défaut validés le 20 octobre 2026 (tableau du skill du projet).

    [Fact]
    public void Agent_creates_parties_and_sees_balances_but_manages_nothing()
    {
        var granted = RolePermissions.For(RoleTemplate.Agent);

        Assert.Contains(Permissions.TransfersCreate, granted);
        Assert.Contains(Permissions.TransfersCredit, granted);
        Assert.Contains(Permissions.TransfersDeliver, granted);
        Assert.Contains(Permissions.CashCreate, granted);
        Assert.Contains(Permissions.PartiesCreate, granted);
        Assert.Contains(Permissions.PartiesViewBalance, granted);
        Assert.Contains(Permissions.CashBudgetRead, granted);
        Assert.DoesNotContain(Permissions.PartiesManage, granted);
        Assert.DoesNotContain(Permissions.TreasuryManage, granted);
    }

    [Fact]
    public void Cashier_reads_the_cash_budget_but_not_party_balances()
    {
        var granted = RolePermissions.For(RoleTemplate.Cashier);

        Assert.Contains(Permissions.CashBudgetRead, granted);
        Assert.Contains(Permissions.TransfersDeliver, granted);
        Assert.Contains(Permissions.CashCreate, granted);
        Assert.DoesNotContain(Permissions.TransfersCreate, granted);
        Assert.DoesNotContain(Permissions.TransfersCredit, granted);
        Assert.DoesNotContain(Permissions.PartiesViewBalance, granted);
        Assert.DoesNotContain(Permissions.PartiesCreate, granted);
        Assert.DoesNotContain(Permissions.PartiesManage, granted);
    }

    [Fact]
    public void Read_only_reads_balances_and_creates_nothing()
    {
        var granted = RolePermissions.For(RoleTemplate.ReadOnly);

        Assert.Contains(Permissions.PartiesViewBalance, granted);
        Assert.Contains(Permissions.CashBudgetRead, granted);
        Assert.DoesNotContain(Permissions.TransfersCreate, granted);
        Assert.DoesNotContain(Permissions.TransfersDeliver, granted);
        Assert.DoesNotContain(Permissions.CashCreate, granted);
        Assert.DoesNotContain(Permissions.PartiesCreate, granted);
        Assert.DoesNotContain(Permissions.PartiesManage, granted);
        Assert.DoesNotContain(Permissions.TreasuryManage, granted);
    }

    [Fact]
    public void Every_role_is_covered()
    {
        foreach (var role in Enum.GetValues<RoleTemplate>())
        {
            Assert.NotNull(RolePermissions.For(role));
        }
    }
}