namespace Identity.Application.Features.Users;

/// <summary>
/// Lit une valeur d'énumération envoyée par l'API (« Agent », « supervisor », « Blocked »…).
/// Partagé par la création d'un compte, le changement de rôle et la liste, pour que tous
/// acceptent exactement les mêmes valeurs.
/// </summary>
internal static class EnumNameParser
{
    // Refuse les nombres (« 2 ») qu'Enum.TryParse accepterait : seul le nom est valable.
    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && !char.IsAsciiDigit(value.Trim()[0])
            && Enum.TryParse(value.Trim(), ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }
}
