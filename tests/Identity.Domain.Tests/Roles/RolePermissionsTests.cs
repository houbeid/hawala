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
    }

    [Fact]
    public void Agent_can_create_a_party_but_not_modify_one()
    {
        var granted = RolePermissions.For(RoleTemplate.Agent);

        Assert.Contains(Permissions.PartiesCreate, granted);
        Assert.DoesNotContain(Permissions.PartiesManage, granted);
        Assert.DoesNotContain(Permissions.PartiesViewBalance, granted);
        Assert.DoesNotContain(Permissions.TreasuryManage, granted);
        Assert.DoesNotContain(Permissions.CashBudgetRead, granted);
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