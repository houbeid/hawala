using System.Security.Cryptography;
using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Credentials;

/// <summary>
/// Mot de passe temporaire au format « K7m-Q2x-9Pd » : 9 caractères aléatoires en 3 groupes,
/// faciles à dicter ou à recopier (pas de 0/O, 1/l/I), avec au moins un chiffre.
/// Tirage par générateur cryptographique.
/// </summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Alphabet = Letters + Digits;
    private const int GroupCount = 3;
    private const int GroupLength = 3;

    public string Generate()
    {
        var characters = new char[GroupCount * GroupLength];
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        // Garantit au moins un chiffre, à une position aléatoire (politique : RequireDigit).
        characters[RandomNumberGenerator.GetInt32(characters.Length)] =
            Digits[RandomNumberGenerator.GetInt32(Digits.Length)];

        return string.Join(
            '-',
            Enumerable.Range(0, GroupCount).Select(g => new string(characters, g * GroupLength, GroupLength)));
    }
}
