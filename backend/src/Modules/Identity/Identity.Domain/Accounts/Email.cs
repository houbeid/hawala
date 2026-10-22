using Hawala.SharedKernel.Results;

namespace Identity.Domain.Accounts;

/// <summary>
/// Adresse email d'un compte : obligatoire, unique, sert d'identifiant de connexion.
/// Toujours stockée normalisée (sans espaces autour, en minuscules) pour que
/// « Ali@Mail.com » et « ali@mail.com » soient le même compte.
/// </summary>
public sealed record Email
{
    public const int MaxLength = 254;
    private const int MaxLocalPartLength = 64;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Email.Empty;
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
        {
            return IdentityErrors.Email.TooLong;
        }

        return IsWellFormed(normalized)
            ? new Email(normalized)
            : IdentityErrors.Email.Invalid;
    }

    public override string ToString() => Value;

    // Contrôle volontairement simple : la vraie preuve qu'un email existe, c'est qu'il reçoit
    // des messages. Ici on refuse seulement les saisies manifestement fausses.
    private static bool IsWellFormed(string email)
    {
        if (email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        var local = email[..at];
        var domain = email[(at + 1)..];

        if (local.Length > MaxLocalPartLength)
        {
            return false;
        }

        if (domain.Length == 0 || domain.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.StartsWith('-') || label.EndsWith('-'))
            {
                return false;
            }
        }

        return labels[^1].Length >= 2;
    }
}