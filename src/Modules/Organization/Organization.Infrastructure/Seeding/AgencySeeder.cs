using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Organization.Domain.Agencies;
using Organization.Infrastructure.Persistence;

namespace Organization.Infrastructure.Seeding;

/// <summary>
/// Crée les deux agences au démarrage si elles n'existent pas encore (recherche par code).
/// Les couleurs sont des valeurs de départ : le Superviseur peut les changer ensuite,
/// et le seed ne les écrase jamais.
/// </summary>
internal sealed class AgencySeeder(OrganizationDbContext db, IClock clock, ILogger<AgencySeeder> logger)
{
    private static readonly (string Code, string Name, Country Country, string Color, string TimeZoneId)[] Defaults =
    [
        ("NKC", "Nouakchott", Country.Mauritania, "#1E8E3E", "Africa/Nouakchott"),
        ("DKR", "Dakar", Country.Senegal, "#1565C0", "Africa/Dakar"),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existingCodes = await db.Agencies.Select(a => a.Code).ToListAsync(cancellationToken);

        foreach (var (code, name, country, color, timeZoneId) in Defaults)
        {
            if (existingCodes.Contains(code))
            {
                continue;
            }

            var agency = Agency.Create(
                code, name, country, AgencyColor.Create(color).Value, timeZoneId, clock.UtcNow).Value;

            db.Agencies.Add(agency);
            logger.LogInformation("Agence {AgencyCode} créée.", code);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}