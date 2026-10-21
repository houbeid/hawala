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

    private static readonly Assembly LedgerContracts = typeof(Ledger.Contracts.ILedger).Assembly;
    private static readonly Assembly LedgerDomain = typeof(Ledger.Domain.LedgerErrors).Assembly;
    private static readonly Assembly LedgerApplication = typeof(Ledger.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly LedgerInfrastructure = typeof(Ledger.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly TreasuryDomain = typeof(Treasury.Domain.TreasuryErrors).Assembly;
    private static readonly Assembly TreasuryApplication = typeof(Treasury.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly TreasuryInfrastructure = typeof(Treasury.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly PartiesDomain = typeof(Parties.Domain.PartiesErrors).Assembly;
    private static readonly Assembly PartiesApplication = typeof(Parties.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly PartiesInfrastructure = typeof(Parties.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly RatesDomain = typeof(Rates.Domain.RatesErrors).Assembly;
    private static readonly Assembly RatesApplication = typeof(Rates.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly RatesInfrastructure = typeof(Rates.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly TreasuryContracts = typeof(Treasury.Contracts.ITreasuryDirectory).Assembly;
    private static readonly Assembly TransfersContracts = typeof(Transfers.Contracts.ITransferMonitor).Assembly;
    private static readonly Assembly PartiesContracts = typeof(Parties.Contracts.IPartyCreditAccounts).Assembly;
    private static readonly Assembly RatesContracts = typeof(Rates.Contracts.IDailyRates).Assembly;

    private static readonly Assembly TransfersDomain = typeof(Transfers.Domain.TransfersErrors).Assembly;
    private static readonly Assembly TransfersApplication = typeof(Transfers.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly TransfersInfrastructure = typeof(Transfers.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] TransfersModule = [TransfersDomain, TransfersApplication, TransfersInfrastructure];

    private static readonly Assembly CashDomain = typeof(Cash.Domain.CashErrors).Assembly;
    private static readonly Assembly CashApplication = typeof(Cash.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly CashInfrastructure = typeof(Cash.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] CashModule = [CashDomain, CashApplication, CashInfrastructure];

    private static readonly Assembly ProfitDomain = typeof(Profit.Domain.ProfitErrors).Assembly;
    private static readonly Assembly ProfitApplication = typeof(Profit.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly ProfitInfrastructure = typeof(Profit.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] ProfitModule = [ProfitDomain, ProfitApplication, ProfitInfrastructure];

    private static readonly Assembly BalanceDomain = typeof(Balance.Domain.BalanceErrors).Assembly;
    private static readonly Assembly BalanceApplication = typeof(Balance.Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly BalanceInfrastructure = typeof(Balance.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] BalanceModule = [BalanceDomain, BalanceApplication, BalanceInfrastructure];

    private static readonly Assembly CashContracts = typeof(Cash.Contracts.ICashBudgets).Assembly;
    private static readonly Assembly ProfitContracts = typeof(Profit.Contracts.IProfitHistory).Assembly;

    private static readonly Assembly[] IdentityModule = [IdentityDomain, IdentityApplication, IdentityInfrastructure];

    private static readonly Assembly[] OrganizationModule =
        [OrganizationContracts, OrganizationDomain, OrganizationApplication, OrganizationInfrastructure];

    private static readonly Assembly[] LedgerModule =
        [LedgerContracts, LedgerDomain, LedgerApplication, LedgerInfrastructure];

    private static readonly Assembly[] TreasuryModule = [TreasuryDomain, TreasuryApplication, TreasuryInfrastructure];

    private static readonly Assembly[] PartiesModule = [PartiesDomain, PartiesApplication, PartiesInfrastructure];

    private static readonly Assembly[] RatesModule = [RatesDomain, RatesApplication, RatesInfrastructure];

    private static readonly Assembly[] DomainLayers =
        [IdentityDomain, OrganizationDomain, LedgerDomain, TreasuryDomain, PartiesDomain, RatesDomain, TransfersDomain, CashDomain, ProfitDomain, BalanceDomain];

    private static readonly Assembly[] ApplicationLayers =
        [IdentityApplication, OrganizationApplication, LedgerApplication, TreasuryApplication, PartiesApplication, RatesApplication, TransfersApplication, CashApplication, ProfitApplication, BalanceApplication];

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
    public void Ledger_depends_on_no_other_module()
    {
        foreach (var assembly in LedgerModule)
        {
            AssertNoReference(assembly, name =>
                name.StartsWith("Identity.", StringComparison.Ordinal)
                || name.StartsWith("Organization.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Treasury_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in TreasuryModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Ledger.", StringComparison.Ordinal) && name != "Ledger.Contracts"));
        }
    }

    [Fact]
    public void Nobody_depends_on_Treasury_yet()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(LedgerModule).Concat(PartiesModule))
        {
            AssertNoReference(assembly, name => name.StartsWith("Treasury.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Parties_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in PartiesModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Ledger.", StringComparison.Ordinal) && name != "Ledger.Contracts")
                || name.StartsWith("Treasury.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Rates_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in RatesModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || name.StartsWith("Organization.", StringComparison.Ordinal)
                || name.StartsWith("Ledger.", StringComparison.Ordinal)
                || name.StartsWith("Treasury.", StringComparison.Ordinal)
                || name.StartsWith("Parties.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Nobody_depends_on_Rates_yet()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(LedgerModule).Concat(TreasuryModule).Concat(PartiesModule))
        {
            AssertNoReference(assembly, name => name.StartsWith("Rates.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Transfers_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in TransfersModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Ledger.", StringComparison.Ordinal) && name != "Ledger.Contracts")
                || (name.StartsWith("Treasury.", StringComparison.Ordinal) && name != "Treasury.Contracts")
                || (name.StartsWith("Rates.", StringComparison.Ordinal) && name != "Rates.Contracts")
                || (name.StartsWith("Parties.", StringComparison.Ordinal) && name != "Parties.Contracts"));
        }
    }

    [Fact]
    public void Cash_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in CashModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Ledger.", StringComparison.Ordinal) && name != "Ledger.Contracts")
                || (name.StartsWith("Treasury.", StringComparison.Ordinal) && name != "Treasury.Contracts")
                || (name.StartsWith("Parties.", StringComparison.Ordinal) && name != "Parties.Contracts")
                || (name.StartsWith("Transfers.", StringComparison.Ordinal) && name != "Transfers.Contracts")
                || name.StartsWith("Rates.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Profit_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in ProfitModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Transfers.", StringComparison.Ordinal) && name != "Transfers.Contracts")
                || name.StartsWith("Ledger.", StringComparison.Ordinal)
                || name.StartsWith("Cash.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Balance_sees_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in BalanceModule)
        {
            AssertNoReference(assembly, name =>
                (name.StartsWith("Identity.", StringComparison.Ordinal) && name != "Identity.Contracts")
                || (name.StartsWith("Organization.", StringComparison.Ordinal) && name != "Organization.Contracts")
                || (name.StartsWith("Ledger.", StringComparison.Ordinal) && name != "Ledger.Contracts")
                || (name.StartsWith("Rates.", StringComparison.Ordinal) && name != "Rates.Contracts")
                || (name.StartsWith("Cash.", StringComparison.Ordinal) && name != "Cash.Contracts")
                || (name.StartsWith("Profit.", StringComparison.Ordinal) && name != "Profit.Contracts")
                || name.StartsWith("Transfers.", StringComparison.Ordinal)
                || name.StartsWith("Treasury.", StringComparison.Ordinal)
                || name.StartsWith("Parties.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Nobody_depends_on_Cash_yet()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(LedgerModule)
                     .Concat(TreasuryModule).Concat(PartiesModule).Concat(RatesModule).Concat(TransfersModule))
        {
            AssertNoReference(assembly, name => name.StartsWith("Cash.", StringComparison.Ordinal) && name != "Cash.Contracts");
        }
    }

    [Fact]
    public void Nobody_depends_on_Transfers_yet()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(LedgerModule)
                     .Concat(TreasuryModule).Concat(PartiesModule).Concat(RatesModule))
        {
            AssertNoReference(assembly, name => name.StartsWith("Transfers.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Nobody_depends_on_Parties_yet()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(LedgerModule).Concat(TreasuryModule))
        {
            AssertNoReference(assembly, name => name.StartsWith("Parties.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Other_modules_see_Ledger_only_through_its_contracts()
    {
        foreach (var assembly in IdentityModule.Concat(OrganizationModule).Concat(TreasuryModule).Concat(PartiesModule))
        {
            AssertNoReference(assembly, name =>
                name.StartsWith("Ledger.", StringComparison.Ordinal)
                && name != "Ledger.Contracts");
        }
    }

    [Fact]
    public void Domain_layers_do_not_depend_on_any_framework()
    {
        foreach (var assembly in DomainLayers)
        {
            AssertNoReference(assembly, IsFramework);
        }
    }

    [Fact]
    public void Application_layers_do_not_depend_on_the_database_or_the_web()
    {
        foreach (var assembly in ApplicationLayers)
        {
            AssertNoReference(assembly, IsFramework);
        }
    }

    [Fact]
    public void Shared_infrastructure_is_never_used_by_a_domain_or_an_application()
    {
        foreach (var assembly in DomainLayers.Concat(ApplicationLayers))
        {
            AssertNoReference(assembly, name => name == "Hawala.SharedInfrastructure");
        }
    }

    [Fact]
    public void Contracts_do_not_depend_on_their_own_module_internals()
    {
        AssertNoReference(OrganizationContracts, name =>
            name is "Organization.Domain" or "Organization.Application" or "Organization.Infrastructure");
        AssertNoReference(LedgerContracts, name =>
            name is "Ledger.Domain" or "Ledger.Application" or "Ledger.Infrastructure");
        AssertNoReference(TreasuryContracts, name =>
            name is "Treasury.Domain" or "Treasury.Application" or "Treasury.Infrastructure");
        AssertNoReference(CashContracts, name => name is "Cash.Domain" or "Cash.Application" or "Cash.Infrastructure");
        AssertNoReference(ProfitContracts, name => name is "Profit.Domain" or "Profit.Application" or "Profit.Infrastructure");
        AssertNoReference(TransfersContracts, name =>
            name is "Transfers.Domain" or "Transfers.Application" or "Transfers.Infrastructure");
        AssertNoReference(PartiesContracts, name =>
            name is "Parties.Domain" or "Parties.Application" or "Parties.Infrastructure");
        AssertNoReference(RatesContracts, name =>
            name is "Rates.Domain" or "Rates.Application" or "Rates.Infrastructure");
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