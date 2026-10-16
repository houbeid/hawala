using System.Text;
using Hawala.SharedKernel.Results;

namespace Identity.Domain.Accounts;

/// <summary>
/// Numéro de téléphone d'un compte : facultatif, unique s'il est renseigné,
/// permet aussi de se connecter. Stocké au format international E.164 (+22222123456).
/// </summary>
public sealed record PhoneNumber
{
    // E.164 : au plus 15 chiffres, indicatif pays compris. 8 chiffres minimum pour rester réaliste.
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    private PhoneNumber(string value) => Value = value;

    /// <summary>Numéro E.164, ex. « +22222123456 ».</summary>
    public string Value { get; }

    /// <summary>
    /// Accepte les saisies courantes : espaces, tirets, points, parenthèses,
    /// et le préfixe « 00 » à la place de « + » (00222… → +222…).
    /// L'indicatif pays est obligatoire.
    /// </summary>
    public static Result<PhoneNumber> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Phone.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var c in input.Trim())
        {
            if (char.IsAsciiDigit(c) || c == '+')
            {
                builder.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return IdentityErrors.Phone.Invalid;
            }
        }

        var compact = builder.ToString();
        if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = "+" + compact[2..];
        }

        return IsE164(compact)
            ? new PhoneNumber(compact)
            : IdentityErrors.Phone.Invalid;
    }

    public override string ToString() => Value;

    private static bool IsE164(string value)
    {
        if (value.Length < 2 || value[0] != '+' || value[1] == '0')
        {
            return false;
        }

        var digits = value.AsSpan(1);
        if (digits.Length is < MinDigits or > MaxDigits)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}