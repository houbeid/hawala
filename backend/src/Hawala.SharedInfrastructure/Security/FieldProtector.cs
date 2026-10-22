using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hawala.SharedInfrastructure.Security;

/// <summary>
/// Chiffrement au repos des données sensibles (phase 6, règle validée le 22 octobre 2026) : téléphone et NNI des tiers,
/// pièce d'identité d'une livraison sans code.
/// <list type="bullet">
/// <item>AES-256-GCM, un nonce aléatoire par valeur : la même valeur donne deux textes chiffrés différents ;</item>
/// <item>une empreinte HMAC-SHA256 (déterministe) sert à l'unicité et à la recherche sur la valeur exacte ;</item>
/// <item>les deux clés dérivent (HKDF) d'une seule clé maîtresse, <c>Encryption:Key</c> (32 octets en base64),
/// dans les user-secrets en dev et une variable d'environnement en prod, sauvegardée hors du serveur :
/// la perdre, c'est perdre ces données.</item>
/// </list>
/// Une valeur sans le préfixe « v1: » est une ancienne valeur en clair : elle est relue telle quelle, puis chiffrée
/// au démarrage (voir l'initialisation des modules concernés).
/// POINT OUVERT : rotation de la clé (le préfixe de version la permettra : « v2: » avec une nouvelle clé).
/// </summary>
public sealed class FieldProtector
{
    public const string ConfigurationKey = "Encryption:Key";
    public const int MasterKeyLength = 32;

    private const string Prefix = "v1:";
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _indexKey;

    public FieldProtector(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        if (masterKey.Length < MasterKeyLength)
        {
            throw new ArgumentException($"La clé maîtresse doit faire au moins {MasterKeyLength} octets.", nameof(masterKey));
        }

        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "hawala/field-encryption/v1"u8.ToArray());
        _indexKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "hawala/field-index/v1"u8.ToArray());
    }

    /// <summary>Lit la clé maîtresse dans la configuration ; absente ou trop courte : le démarrage échoue avec un message clair.</summary>
    public static FieldProtector FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[ConfigurationKey];
        byte[] key;
        try
        {
            key = string.IsNullOrWhiteSpace(value) ? [] : Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            key = [];
        }

        if (key.Length < MasterKeyLength)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} absente ou invalide : {MasterKeyLength} octets aléatoires en base64 " +
                "(user-secrets en dev, variable d'environnement Encryption__Key en prod).");
        }

        return new FieldProtector(key);
    }

    public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);

        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var buffer = new byte[NonceLength + plainBytes.Length + TagLength];
        var nonce = buffer.AsSpan(0, NonceLength);
        var cipher = buffer.AsSpan(NonceLength, plainBytes.Length);
        var tag = buffer.AsSpan(NonceLength + plainBytes.Length, TagLength);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encryptionKey, TagLength);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Prefix + Convert.ToBase64String(buffer);
    }

    /// <summary>Déchiffre ; une ancienne valeur en clair (sans préfixe) est renvoyée telle quelle.</summary>
    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!IsProtected(stored))
        {
            return stored;
        }

        var buffer = Convert.FromBase64String(stored[Prefix.Length..]);
        if (buffer.Length < NonceLength + TagLength)
        {
            throw new CryptographicException("Valeur chiffrée tronquée.");
        }

        var cipherLength = buffer.Length - NonceLength - TagLength;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(_encryptionKey, TagLength);
        aes.Decrypt(
            buffer.AsSpan(0, NonceLength),
            buffer.AsSpan(NonceLength, cipherLength),
            buffer.AsSpan(NonceLength + cipherLength, TagLength),
            plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Empreinte déterministe (hexadécimal, 64 caractères) d'une valeur déjà normalisée, pour l'unicité.</summary>
    public string Index(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_indexKey, Encoding.UTF8.GetBytes(plain)));
    }

    /// <summary>Longueur d'une colonne qui garde une valeur chiffrée de <paramref name="plainMaxLength"/> caractères au plus.</summary>
    public static int StoredLength(int plainMaxLength)
    {
        // Pire cas : 3 octets UTF-8 par caractère, plus le nonce et l'étiquette, encodés en base64, plus le préfixe.
        var bytes = (plainMaxLength * 3) + NonceLength + TagLength;
        return Prefix.Length + (((bytes + 2) / 3) * 4);
    }

    public const int IndexLength = 64;
}

public static class FieldProtectionRegistration
{
    /// <summary>Enregistre le chiffrement des champs (une instance pour toute l'app). Seul le premier appel enregistre.</summary>
    public static IServiceCollection AddFieldProtection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(_ => FieldProtector.FromConfiguration(configuration));
        return services;
    }
}
