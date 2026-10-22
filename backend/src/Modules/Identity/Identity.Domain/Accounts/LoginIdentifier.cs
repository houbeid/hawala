using Hawala.SharedKernel.Results;

namespace Identity.Domain.Accounts;

public enum LoginIdentifierKind
{
    Email,
    Phone,
}

/// <summary>
/// Ce que l'agent tape dans le champ unique « Email ou téléphone » de l'écran de connexion.
/// Contient un « @ » → email ; sinon → téléphone.
/// </summary>
public sealed record LoginIdentifier
{
    private LoginIdentifier(LoginIdentifierKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public LoginIdentifierKind Kind { get; }

    /// <summary>Valeur normalisée : email en minuscules ou numéro E.164.</summary>
    public string Value { get; }

    public static Result<LoginIdentifier> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Login.Empty;
        }

        if (input.Contains('@', StringComparison.Ordinal))
        {
            var email = Email.Create(input);
            return email.IsSuccess
                ? new LoginIdentifier(LoginIdentifierKind.Email, email.Value.Value)
                : email.Error;
        }

        var phone = PhoneNumber.Create(input);
        return phone.IsSuccess
            ? new LoginIdentifier(LoginIdentifierKind.Phone, phone.Value.Value)
            : phone.Error;
    }

    public override string ToString() => Value;
}