using System.Text;

namespace Hawala.SharedKernel.Text;

/// <summary>
/// Règle de format des numéros de téléphone, commune à tous les modules (comptes, tiers…) :
/// format international E.164 (+22222123456), indicatif pays obligatoire.
/// Chaque module garde son propre type et ses propres codes d'erreur ; seule la règle est partagée.
/// </summary>
public static class PhoneNumbers
{
    // E.164 : au plus 15 chiffres, indicatif pays compris. 8 chiffres minimum pour rester réaliste.
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    /// <summary>
    /// Accepte les saisies courantes : espaces, tirets, points, parenthèses,
    /// et le préfixe « 00 » à la place de « + » (00222… → +222…).
    /// </summary>
    /// <param name="input">Saisie non vide (le cas « vide » est traité par chaque module).</param>
    /// <param name="normalized">Le numéro E.164, ex. « +22222123456 », si la saisie est valide.</param>
    public static bool TryNormalizeE164(string input, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(input);
        normalized = string.Empty;

        var builder = new StringBuilder(input.Length);
        foreach (var c in input.Trim())
        {
            if (char.IsAsciiDigit(c) || c == '+')
            {
                builder.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return false;
            }
        }

        var compact = builder.ToString();
        if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = "+" + compact[2..];
        }

        if (!IsE164(compact))
        {
            return false;
        }

        normalized = compact;
        return true;
    }

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
