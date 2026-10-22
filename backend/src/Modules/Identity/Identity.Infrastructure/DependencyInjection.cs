using Audit.Contracts;
using System.Text;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Application.Features.Accounts;
using Identity.Application.Features.Accounts.GetCurrentAccount;
using Identity.Application.Features.Users;
using Identity.Application.Features.Users.BlockUser;
using Identity.Application.Features.Users.ChangeUserAgency;
using Identity.Application.Features.Users.ChangeUserRole;
using Identity.Application.Features.Users.CreateUser;
using Identity.Application.Features.Users.GetUser;
using Identity.Application.Features.Users.Limits;
using Identity.Application.Features.Users.ListUsers;
using Identity.Application.Features.Users.PermissionOverrides;
using Identity.Application.Features.Users.ResetUserPassword;
using Identity.Application.Features.Users.UnblockUser;
using Identity.Application.Features.Users.UpdateUserProfile;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Sessions;
using Identity.Application.Features.Auth.ChangePassword;
using Identity.Application.Features.Auth.Login;
using Identity.Application.Features.Auth.Logout;
using Identity.Application.Features.Auth.Refresh;
using Identity.Contracts;
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

        // Connexion partagée de la requête (phase 6) : les commandes d'Identité écrivent leur ligne d'audit
        // dans la même transaction. Login et Refresh, sans décorateur, enregistrent toujours tout de suite.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<IdentityModuleDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    sp.GetRequiredService<SharedConnection>().Connection,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityModuleDbContext.Schema))
                .AddInterceptors(sp.GetRequiredService<AccountCacheInvalidationInterceptor>()));

        services.AddScoped<SessionValidator>();
        services.AddScoped<AgencyScope>();
        services.AddScoped<IAgencyScope>(sp => sp.GetRequiredService<AgencyScope>());
        services.AddScoped<ICurrentAccount>(sp => sp.GetRequiredService<AgencyScope>());
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddHttpContextAccessor();
        services.AddScoped<IClientInfo, HttpClientInfo>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityModuleDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<IdentityModuleDbContext>());
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
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
    }

    private static void AddHandlers(IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<LoginCommand, AuthTokens>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshCommand, AuthTokens>, RefreshCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutCommandHandler>();
        services.AddScoped<IQueryHandler<GetCurrentAccountQuery, CurrentAccount>, GetCurrentAccountQueryHandler>();
        services.AddScoped<IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>>, ListUsersQueryHandler>();
        services.AddScoped<IQueryHandler<GetUserQuery, UserSummary>, GetUserQueryHandler>();
        services.AddScoped<IQueryHandler<GetUserPermissionsQuery, UserPermissions>, GetUserPermissionsQueryHandler>();

        // Une seule table et pas de verrou, mais la ligne d'audit doit être dans la même transaction (phase 6).
        services.AddSharedTransactional<ChangeUserAgencyCommand, ChangeUserAgencyCommandHandler>();
        services.AddSharedTransactional<UpdateUserProfileCommand, UpdateUserProfileCommandHandler>();

        // Ces commandes modifient les tables d'ASP.NET Identity et les nôtres : une seule transaction
        // (transaction commune depuis la phase 6, pour y écrire aussi la ligne d'audit).
        services.AddSharedTransactional<ChangePasswordCommand, AuthTokens, ChangePasswordCommandHandler>();
        services.AddSharedTransactional<CreateUserCommand, CreatedUser, CreateUserCommandHandler>();
        services.AddSharedTransactional<UnblockUserCommand, UnblockedUser, UnblockUserCommandHandler>();
        services.AddSharedTransactional<ResetUserPasswordCommand, ResetPassword, ResetUserPasswordCommandHandler>();

        // Pas de table d'Identity ici, mais le verrou sur les Superviseurs exige une transaction.
        services.AddSharedTransactional<BlockUserCommand, BlockUserCommandHandler>();
        services.AddSharedTransactional<ChangeUserRoleCommand, ChangeUserRoleCommandHandler>();

        // Le compte et sa ligne d'audit dans la même transaction.
        services.AddSharedTransactional<SetUserPermissionsCommand, UserPermissions, SetUserPermissionsCommandHandler>();
        services.AddSharedTransactional<SetUserLimitsCommand, UserLimits, UserLimitsHandlers>();

        // Fermer une session : ses jetons et, si quelqu'un d'autre la ferme, la ligne d'audit.
        services.AddSharedTransactional<CloseSessionCommand, SessionHandlers>();
        services.AddScoped<IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionView>>>(sp => sp.GetRequiredService<SessionHandlers>());
        services.AddScoped<IQueryHandler<GetUserLimitsQuery, UserLimits>>(sp => sp.GetRequiredService<UserLimitsHandlers>());
    }
}
