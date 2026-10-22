namespace Hawala.Api.Configuration;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Seule politique qui accepte un compte au mot de passe temporaire non changé :
    /// réservée à l'endpoint de changement de mot de passe.
    /// </summary>
    public const string PasswordChangeAllowed = "PasswordChangeAllowed";
}