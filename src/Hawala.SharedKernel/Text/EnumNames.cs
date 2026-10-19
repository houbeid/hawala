namespace Hawala.SharedKernel.Text;

/// <summary>
/// Lit une valeur d'énumération envoyée par l'API en texte (« Agent », « supervisor », « Cash »…).
/// Partagé par tous les modules, pour qu'ils acceptent exactement les mêmes écritures.
/// </summary>
public static class EnumNames
{
    /// <summary>Insensible à la casse ; refuse les nombres (« 2 ») qu'Enum.TryParse accepterait : seul le nom est valable.</summary>
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
