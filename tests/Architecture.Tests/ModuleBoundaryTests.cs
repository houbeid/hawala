using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// Règles d'architecture vérifiées à chaque build :
/// un module ne voit que les Contracts d'un autre, et le Domain ne dépend d'aucun framework.
/// Un échec ici signifie qu'une référence interdite a été ajoutée : la retirer, ne pas assouplir le test.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly IdentityDomain = typeof(Identity.Domain.IdentityErrors).Assembly;
    private static readonly Assembly IdentityApplication = typeof(Identity.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly IdentityInfrastructure = typeof(Identity.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly OrganizationContracts = typeof(Organization.Contracts.AgencySummary).Assembly;
    private static readonly Assembly OrganizationDomain = typeof(Organization.Domain.OrganizationErrors).Assembly;
    private static readonly Assembly OrganizationApplication = typeof(Organization.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly OrganizationInfrastructure = typeof(Organization.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] IdentityModule = [IdentityDomain, IdentityApplication, IdentityInfrastructure];

    private static readonly Assembly[] OrganizationModule =
        [OrganizationContracts, OrganizationDomain, OrganizationApplication, OrganizationInfrastructure];

    [Fact]
    public void Identity_sees_Organization_only_through_its_contracts()
    {
        foreach (var assembly in IdentityModule)
        {
            AssertNoReference(assembly, name =>
                name.StartsWith("Organization.", StringComparison.Ordinal)
                && name != "Organization.Contracts");
        }
    }

    [Fact]
    public void Organization_does_not_depend_on_Identity()
    {
        foreach (var assembly in OrganizationModule)
        {
            AssertNoReference(assembly, name => name.StartsWith("Identity.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Domain_layers_do_not_depend_on_any_framework()
    {
        foreach (var assembly in new[] { IdentityDomain, OrganizationDomain })
        {
            AssertNoReference(assembly, IsFramework);
        }
    }

    [Fact]
    public void Application_layers_do_not_depend_on_the_database_or_the_web()
    {
        foreach (var assembly in new[] { IdentityApplication, OrganizationApplication })
        {
            AssertNoReference(assembly, IsFramework);
        }
    }

    [Fact]
    public void Contracts_do_not_depend_on_their_own_module_internals()
    {
        AssertNoReference(OrganizationContracts, name =>
            name is "Organization.Domain" or "Organization.Application" or "Organization.Infrastructure");
    }

    private static bool IsFramework(string name) =>
        name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
        || name.StartsWith("Npgsql", StringComparison.Ordinal);

    private static void AssertNoReference(Assembly assembly, Func<string, bool> isForbidden)
    {
        var forbidden = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(isForbidden)
            .ToList();

        Assert.True(
            forbidden.Count == 0,
            $"{assembly.GetName().Name} ne doit pas référencer : {string.Join(", ", forbidden)}");
    }
}