using System.Globalization;
using System.Security.Cryptography;

namespace Transfers.Application.Features.Transfers;

/// <summary>Code de retrait (règle validée) : 6 chiffres aléatoires, tirés par un générateur cryptographique.</summary>
public static class WithdrawalCodes
{
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
