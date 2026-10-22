using System.Security.Cryptography;
using System.Text;
using Transfers.Application.Abstractions;

namespace Transfers.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 avec un sel aléatoire par transfert, au format « v1.{itérations}.{sel}.{hash} » (Base64).
/// Un code de 6 chiffres reste devinable hors ligne : la vraie protection est la limite de 5 essais à la livraison
/// et l'accès restreint à la base ; le hachage évite surtout qu'un code soit lisible en clair.
/// </summary>
internal sealed class Pbkdf2WithdrawalCodeHasher : IWithdrawalCodeHasher
{
    private const string Version = "v1";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(code, salt, Iterations);
        return string.Join('.', Version, Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string code, string hash)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(hash);

        var parts = hash.Split('.');
        if (parts.Length != 4
            || parts[0] != Version
            || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        return CryptographicOperations.FixedTimeEquals(Derive(code, salt, iterations), expected);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, HashSize);
}
