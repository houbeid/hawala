using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Transfers.Application.Abstractions;

namespace Transfers.Infrastructure.Persistence.Repositories;

/// <summary>
/// Compteur atomique par agence et par jour : deux créations simultanées n'obtiennent jamais le même numéro
/// (la ligne reste verrouillée jusqu'à la fin de la transaction de la commande).
/// POINT OUVERT : format provisoire « TR-{code agence}-{aaaammjj}-{numéro sur 6 chiffres} », en attendant le sens
/// des codes de l'app actuelle (RS18, HG01, TGNC…).
/// </summary>
internal sealed class TransferReferenceGenerator(TransfersDbContext db) : ITransferReferenceGenerator
{
    public async Task<string> NextAsync(Guid agencyId, string agencyCode, DateOnly day, CancellationToken cancellationToken)
    {
        var values = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO transfers.reference_counters ("AgencyId", "Day", "LastValue")
                VALUES ({agencyId}, {day}, 1)
                ON CONFLICT ("AgencyId", "Day") DO UPDATE
                    SET "LastValue" = reference_counters."LastValue" + 1
                RETURNING "LastValue" AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"TR-{agencyCode}-{day:yyyyMMdd}-{values.Single():D6}");
    }
}
