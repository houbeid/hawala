using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Organization.Application.Abstractions;
using Organization.Application.Agencies;
using Organization.Application.Agencies.GetAgencies;
using Organization.Contracts;
using Organization.Infrastructure.Persistence;
using Organization.Infrastructure.Persistence.Repositories;
using Organization.Infrastructure.Seeding;

namespace Organization.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre tout le module Organisation. Appelé une fois par l'API dans Program.cs.</summary>
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddDbContext<OrganizationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", OrganizationDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrganizationDbContext>());
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IAgencyDirectory, AgencyDirectory>();
        services.AddScoped<AgencySeeder>();

        services.AddScoped<IQueryHandler<GetAgenciesQuery, IReadOnlyList<AgencySummary>>, GetAgenciesQueryHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « organization », puis création des deux agences si besoin.</summary>
    public static async Task InitializeOrganizationModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<AgencySeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}