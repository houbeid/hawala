using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Tokens;

/// <summary>
/// Paramètres du JWT, section « Jwt » de la configuration.
/// La clé de signature ne doit JAMAIS être dans appsettings.json versionné :
/// en dev via « dotnet user-secrets », en production via une variable d'environnement.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 exige une clé d'au moins 32 octets.</summary>
    public const int MinimumKeyLength = 32;

    public string Issuer { get; init; } = "hawala-api";

    public string Audience { get; init; } = "hawala-app";

    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Clé de signature, utilisée à la fois pour émettre (Infrastructure) et pour valider (API) :
    /// une seule façon de la construire. Échoue au démarrage si la clé est absente ou trop courte.
    /// </summary>
    public SymmetricSecurityKey CreateSigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey doit faire au moins {MinimumKeyLength} octets. " +
                "La définir avec dotnet user-secrets (dev) ou une variable d'environnement (prod).");
        }

        return new SymmetricSecurityKey(bytes);
    }
}