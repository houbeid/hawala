namespace Identity.Infrastructure.Seeding;

/// <summary>
/// Premier Superviseur, créé au démarrage s'il n'existe encore aucun Superviseur.
/// Section « SeedSupervisor » de la configuration. Le mot de passe temporaire ne doit jamais être
/// dans appsettings.json versionné : dotnet user-secrets en dev, variable d'environnement en prod.
/// Il devra être changé à la première connexion.
/// </summary>
public sealed class SupervisorSeedOptions
{
    public const string SectionName = "SeedSupervisor";

    public string Email { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    /// <summary>Facultatif, format international.</summary>
    public string? Phone { get; init; }

    public string TemporaryPassword { get; init; } = string.Empty;
}