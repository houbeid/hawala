using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Démarre la vraie API en mémoire, branchée sur le PostgreSQL jetable du test.
/// Environnement « Testing » : ni appsettings.Development.json ni les user-secrets ne sont chargés,
/// donc les tests n'utilisent jamais ta vraie configuration.
/// </summary>
public sealed class HawalaApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string SeedSupervisorEmail = "superviseur.test@example.com";
    public const string SeedSupervisorPassword = "Temporaire1";

    // Clé fictive, réservée aux tests (au moins 32 octets).
    private const string TestSigningKey = "cle-de-test-uniquement-0123456789-abcdefghijklmnop";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        // UseSetting est appliqué avant que Program.cs lise la configuration.
        builder.UseSetting("ConnectionStrings:Hawala", connectionString);
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("SeedSupervisor:Email", SeedSupervisorEmail);
        builder.UseSetting("SeedSupervisor:FullName", "Superviseur Test");
        builder.UseSetting("SeedSupervisor:TemporaryPassword", SeedSupervisorPassword);
    }
}