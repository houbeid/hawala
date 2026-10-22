namespace Identity.Application.Abstractions;

/// <summary>
/// Durées de vie des jetons, lues depuis la configuration (section « Auth » d'appsettings.json).
/// Valeurs par défaut : jeton d'accès 15 minutes, refresh token 12 heures (une connexion par jour de travail).
/// </summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromHours(12);
}