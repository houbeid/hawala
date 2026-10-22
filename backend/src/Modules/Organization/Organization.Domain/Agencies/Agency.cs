using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Organization.Domain.Agencies;

/// <summary>
/// Agence : Nouakchott ou Dakar. Chaque agence a sa caisse, son budget et ses données,
/// toujours dans la devise de son pays ; les deux ne sont jamais mélangées.
/// Un agent appartient à une seule agence ; le Superviseur voit les deux.
/// </summary>
public sealed class Agency
{
    public const int NameMaxLength = 50;
    private const int CodeLength = 3;

    // Constructeur réservé à EF Core.
    private Agency()
    {
        Code = null!;
        Name = null!;
        Color = null!;
        TimeZoneId = null!;
    }

    private Agency(Guid id, string code, string name, Country country, AgencyColor color, string timeZoneId)
    {
        Id = id;
        Code = code;
        Name = name;
        Country = country;
        Color = color;
        TimeZoneId = timeZoneId;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Code court et définitif (NKC, DKR). Il apparaît dans les références lisibles
    /// (TR-NKC-20261013-000052) : il ne change jamais après la création.
    /// </summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public Country Country { get; private set; }

    /// <summary>Déduite du pays : jamais saisie, donc jamais incohérente.</summary>
    public Currency Currency => Country.Currency();

    public AgencyColor Color { get; private set; }

    /// <summary>
    /// Fuseau IANA (Africa/Nouakchott, Africa/Dakar). Fixe la limite d'une « journée » de l'agence :
    /// budget quotidien, références par jour. Les instants restent stockés en UTC.
    /// </summary>
    public string TimeZoneId { get; private set; }

    public static Result<Agency> Create(
        string? code,
        string? name,
        Country country,
        AgencyColor color,
        string? timeZoneId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(color);

        var normalizedCode = code?.Trim() ?? string.Empty;
        if (normalizedCode.Length != CodeLength || !normalizedCode.All(char.IsAsciiLetterUpper))
        {
            return OrganizationErrors.Agency.CodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return OrganizationErrors.Agency.NameEmpty;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return OrganizationErrors.Agency.NameTooLong;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return OrganizationErrors.Agency.TimeZoneEmpty;
        }

        return new Agency(IdGenerator.NewId(now), normalizedCode, trimmedName, country, color, timeZoneId.Trim());
    }

    /// <summary>Le Superviseur choisit la couleur de chaque agence.</summary>
    public void ChangeColor(AgencyColor color)
    {
        ArgumentNullException.ThrowIfNull(color);
        Color = color;
    }
}