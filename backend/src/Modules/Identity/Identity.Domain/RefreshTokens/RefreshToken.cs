using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Results;

namespace Identity.Domain.RefreshTokens;

/// <summary>
/// Refresh token rotatif. Règles :
/// <list type="bullet">
/// <item>Le jeton en clair n'existe qu'au moment de l'émission ; la base stocke son hash SHA-256.</item>
/// <item>Chaque rafraîchissement révoque le jeton présenté et en émet un nouveau dans la même famille.</item>
/// <item>Présenter un jeton déjà remplacé = vol probable : l'appelant doit révoquer toute la famille.</item>
/// </list>
/// Une famille = une session (une connexion sur un appareil).
/// </summary>
public sealed class RefreshToken
{
    private const int SecretSizeInBytes = 32;

    // Constructeur aussi utilisé par EF Core (les noms des paramètres correspondent aux propriétés).
    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? userAgent,
        string? ipAddress)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UserAgent = userAgent;
        IpAddress = ipAddress;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    /// <summary>Session à laquelle appartient le jeton. Tous les jetons d'une même connexion partagent la famille.</summary>
    public Guid FamilyId { get; }

    /// <summary>SHA-256 du jeton en clair, en hexadécimal minuscule.</summary>
    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Appareil de la requête qui a émis ce jeton (connexion ou dernier rafraîchissement) ; null s'il est inconnu.</summary>
    public string? UserAgent { get; }

    public string? IpAddress { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    /// <summary>Jeton émis à la place de celui-ci lors de la rotation.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    /// <summary>Premier jeton d'une nouvelle session (connexion réussie).</summary>
    /// <param name="device">Appareil de la connexion (phase 6) ; inconnu si absent.</param>
    public static IssuedRefreshToken IssueForNewSession(
        Guid userId, DateTimeOffset now, TimeSpan lifetime, SessionDevice? device = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("L'utilisateur est obligatoire.", nameof(userId));
        }

        return Issue(userId, familyId: IdGenerator.NewId(now), now, lifetime, device ?? SessionDevice.Unknown);
    }

    /// <summary>
    /// Rafraîchissement : révoque ce jeton et en émet un nouveau dans la même famille.
    /// En cas d'erreur <see cref="IdentityErrors.RefreshToken.Reused"/>, l'appelant doit révoquer
    /// tous les jetons actifs de la famille (<see cref="Revoke"/> avec <see cref="RefreshTokenRevocationReason.ReuseDetected"/>).
    /// </summary>
    /// <param name="device">Appareil du rafraîchissement (l'IP peut changer) ; inconnu si absent.</param>
    public Result<IssuedRefreshToken> Rotate(DateTimeOffset now, TimeSpan lifetime, SessionDevice? device = null)
    {
        if (IsRevoked)
        {
            return RevocationReason == RefreshTokenRevocationReason.Rotated
                ? IdentityErrors.RefreshToken.Reused
                : IdentityErrors.RefreshToken.Revoked;
        }

        if (now >= ExpiresAt)
        {
            return IdentityErrors.RefreshToken.Expired;
        }

        var next = Issue(UserId, FamilyId, now, lifetime, device ?? SessionDevice.Unknown);

        Revoke(now, RefreshTokenRevocationReason.Rotated);
        ReplacedByTokenId = next.Token.Id;

        return next;
    }

    /// <summary>
    /// Révoque le jeton. Sans effet s'il l'est déjà : on garde la première raison,
    /// qui est celle dont l'audit a besoin.
    /// </summary>
    public void Revoke(DateTimeOffset now, RefreshTokenRevocationReason reason)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }

    /// <summary>Hash d'un jeton en clair, pour le retrouver en base quand le client le présente.</summary>
    public static string HashOf(string plainValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainValue);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainValue));
        return Convert.ToHexStringLower(hash);
    }

    private static IssuedRefreshToken Issue(
        Guid userId, Guid familyId, DateTimeOffset now, TimeSpan lifetime, SessionDevice device)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "La durée de vie doit être positive.");
        }

        // 32 octets aléatoires (256 bits) issus du générateur cryptographique, encodés pour un cookie.
        var plainValue = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretSizeInBytes));

        var token = new RefreshToken(
            id: IdGenerator.NewId(now),
            userId: userId,
            familyId: familyId,
            tokenHash: HashOf(plainValue),
            createdAt: now,
            expiresAt: now + lifetime,
            userAgent: device.UserAgent,
            ipAddress: device.IpAddress);

        return new IssuedRefreshToken(token, plainValue);
    }
}