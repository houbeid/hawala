using Audit.Application;
using Audit.Application.Abstractions;
using Audit.Application.Features;
using Audit.Contracts;
using Audit.Infrastructure.Persistence;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Audit.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre le journal d'audit. Appelé une fois par l'API dans Program.cs.</summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddHttpContextAccessor();

        // Option A : l'audit écrit sur la connexion partagée, dans la transaction de la commande qu'il trace.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<AuditDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema)));

        services.AddScoped<IAuditStore>(sp => sp.GetRequiredService<AuditDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<AuditDbContext>());
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditReader, AuditReader>();
        services.AddScoped<IQueryHandler<ListAuditEntriesQuery, AuditPage>, ListAuditEntriesQueryHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « audit » (table et déclencheurs d'insertion seule).</summary>
    public static async Task InitializeAuditModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
