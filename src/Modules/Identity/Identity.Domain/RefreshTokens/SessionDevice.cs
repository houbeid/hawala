namespace Identity.Domain.RefreshTokens;

/// <summary>
/// L'appareil d'une session (phase 6) : navigateur ou téléphone (en-tête User-Agent) et adresse IP de la requête.
/// Données techniques venues du client : coupées à leur longueur maximale plutôt que refusées.
/// </summary>
public sealed record SessionDevice
{
    public const int UserAgentMaxLength = 512;

    /// <summary>Une adresse IPv6 complète tient en 45 caractères.</summary>
    public const int IpAddressMaxLength = 45;

    public static readonly SessionDevice Unknown = new(null, null);

    public SessionDevice(string? userAgent, string? ipAddress)
    {
        UserAgent = Clip(userAgent, UserAgentMaxLength);
        IpAddress = Clip(ipAddress, IpAddressMaxLength);
    }

    public string? UserAgent { get; }

    public string? IpAddress { get; }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
