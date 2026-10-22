namespace Identity.Infrastructure.Tokens;

/// <summary>Noms des informations portées par le JWT (option B : l'identité seulement).</summary>
public static class HawalaClaimTypes
{
    /// <summary>Id du compte (claim standard « sub »).</summary>
    public const string Subject = "sub";

    /// <summary>
    /// SecurityVersion du compte au moment de l'émission. Comparée à la base (avec cache) à chaque requête :
    /// si le compte a été bloqué ou a changé de rôle depuis, le jeton est refusé tout de suite.
    /// </summary>
    public const string SecurityVersion = "sv";

    /// <summary>Présent (« true ») tant que le mot de passe temporaire n'a pas été changé.</summary>
    public const string PasswordChangeRequired = "pwd_change";

    /// <summary>
    /// Session (famille de refresh tokens) du jeton, depuis la phase 6 : fermer la session le rend invalide
    /// à la requête suivante. Absent des jetons émis avant (acceptés jusqu'à leur expiration, 15 minutes au plus).
    /// </summary>
    public const string SessionId = "sid";
}