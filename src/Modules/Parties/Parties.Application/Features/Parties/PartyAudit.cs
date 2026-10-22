using System.Globalization;
using Audit.Contracts;
using Hawala.SharedKernel.Results;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties;

/// <summary>
/// Lignes d'audit des réglages d'un tiers (phase 6, tour 7c, règle du 22 octobre 2026), dans la transaction de la commande.
/// Téléphone et NNI sont chiffrés au repos : l'audit ne les recopie jamais en clair, il dit seulement « set » (renseigné),
/// « changed » (remplacé) ou null (absent).
/// </summary>
internal static class PartyAudit
{
    public const string Created = "Parties.Party.Created";
    public const string Updated = "Parties.Party.Updated";
    public const string OpeningBalanceChanged = "Parties.Party.OpeningBalanceChanged";
    public const string CreditLimitChanged = "Parties.Party.CreditLimitChanged";
    public const string Archived = "Parties.Party.Archived";
    public const string Unarchived = "Parties.Party.Unarchived";

    /// <summary>État d'une fiche, pour comparer avant / après (téléphone et NNI masqués).</summary>
    public sealed record Snapshot(
        string Name, string Type, string? Phone, string? Nni, string? Address, string? Note, bool InBalanceSheet)
    {
        public static Snapshot Of(Party party) => new(
            party.Name, party.Type.ToString(), party.Phone?.Value, party.Nni, party.Address, party.Note, party.InBalanceSheet);
    }

    /// <param name="actorId">Auteur, lu dans le JWT ; null pour une action du système.</param>
    /// <param name="agencyId">Agence concernée (solde initial, plafond) ; null pour la fiche, commune aux deux agences.</param>
    public static async Task<Result> RecordAsync(
        IAuditLog audit,
        string action,
        Guid partyId,
        Guid? actorId,
        IEnumerable<AuditChange> changes,
        CancellationToken cancellationToken,
        Guid? agencyId = null)
    {
        var changed = changes.Where(c => !string.Equals(c.Before, c.After, StringComparison.Ordinal)).ToList();
        var recorded = await audit.RecordAsync(
            new AuditRecord(action, "Party", partyId, actorId, agencyId, Changes: changed), cancellationToken);

        return recorded.IsFailure ? recorded.Error : Result.Success();
    }

    /// <summary>Champs de la fiche qui diffèrent ; téléphone et NNI masqués.</summary>
    public static IEnumerable<AuditChange> Diff(Snapshot? before, Snapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);

        yield return new AuditChange("name", before?.Name, after.Name);
        yield return new AuditChange("type", before?.Type, after.Type);
        yield return Masked("phone", before?.Phone, after.Phone);
        yield return Masked("nni", before?.Nni, after.Nni);
        yield return new AuditChange("address", before?.Address, after.Address);
        yield return new AuditChange("note", before?.Note, after.Note);
        yield return new AuditChange("inBalanceSheet", before is null ? null : Text(before.InBalanceSheet), Text(after.InBalanceSheet));
    }

    public static string Text(bool value) => value ? "true" : "false";

    /// <summary>Sans zéros inutiles : un montant relu en base (1000.00) et un montant reçu (1000) s'écrivent pareil.</summary>
    public static string? Text(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>« set » / « changed » / null : jamais la valeur. Deux valeurs égales donnent un champ inchangé (écarté).</summary>
    private static AuditChange Masked(string field, string? before, string? after) =>
        string.Equals(before, after, StringComparison.Ordinal)
            ? new AuditChange(field, null, null)
            : new AuditChange(field, before is null ? null : "set", after is null ? null : before is null ? "set" : "changed");
}
