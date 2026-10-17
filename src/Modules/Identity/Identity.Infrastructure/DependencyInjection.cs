using System.Text;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Application.Features.Accounts.GetCurrentAccount;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.ChangePassword;
using Identity.Application.Features.Auth.Login;
using Identity.Application.Features.Auth.Logout;
using Identity.Application.Features.Auth.Refresh;
using Identity.Infrastructure.Credentials;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Seeding;
using Identity.Infrastructure.Sessions;
using Identity.Infrastructure.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Infrastructure;

public static class DependencyInjection
{
    // Règles validées / proposées pour l'authentification. Les changer ici seulement.
    private const int MaxFailedAccessAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int PasswordMinLength = 8;

    /// <summary>Enregistre tout le module Identité. Appelé une fois par l'API dans Program.cs.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IClock, SystemClock>();

        AddSettings(services, configuration);
        AddPersistence(services, configuration);
        AddCredentials(services);

        services.AddSingleton<IAccessTokenIssuer, JwtTokenService>();
        services.AddScoped<SupervisorSeeder>();

        AddHandlers(services);

        return services;
    }

    /// <summary>
    /// Au démarrage : applique les migrations du schéma « identity », puis crée le premier Superviseur
    /// si nécessaire. Une seule instance de l'API tourne sur le VPS, donc pas de risque de migrations concurrentes.
    /// </summary>
    public static async Task InitializeIdentityModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<SupervisorSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }

    private static void AddSettings(IServiceCollection services, IConfiguration configuration)
    {
        var authSettings = configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();
        services.AddSingleton(authSettings);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumKeyLength,
                $"Jwt:SigningKey absente ou trop courte (minimum {JwtOptions.MinimumKeyLength} octets).")
            .ValidateOnStart();

        services.AddOptions<SupervisorSeedOptions>()
            .Bind(configuration.GetSection(SupervisorSeedOptions.SectionName));
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.AddMemoryCache();
        services.AddSingleton<AccountCacheInvalidationInterceptor>();

        services.AddDbContext<IdentityModuleDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityModuleDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<AccountCacheInvalidationInterceptor>()));

        services.AddScoped<SessionValidator>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityModuleDbContext>());
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    }

    private static void AddCredentials(IServiceCollection services)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

                // Pas de majuscule ni de symbole obligatoire : pénible sur un clavier de téléphone,
                // et la longueur protège mieux que la complexité.
                options.Password.RequiredLength = PasswordMinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                // L'email est géré par UserAccount, pas par ASP.NET Identity.
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>();

        services.AddScoped<ICredentialStore, CredentialStore>();
    }

    private static void AddHandlers(IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<LoginCommand, AuthTokens>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshCommand, AuthTokens>, RefreshCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutCommandHandler>();
        services.AddScoped<IQueryHandler<GetCurrentAccountQuery, CurrentAccount>, GetCurrentAccountQueryHandler>();

        // ChangePassword modifie les tables d'ASP.NET Identity et les nôtres : une seule transaction.
        services.AddScoped<ChangePasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand, AuthTokens>>(sp =>
            new TransactionalCommandHandler<ChangePasswordCommand, AuthTokens>(
                sp.GetRequiredService<ChangePasswordCommandHandler>(),
                sp.GetRequiredService<IdentityModuleDbContext>()));
    }
}