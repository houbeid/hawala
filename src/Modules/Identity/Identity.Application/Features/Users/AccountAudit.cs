using Audit.Contracts;
using Hawala.SharedKernel.Results;
using Identity.Domain.Accounts;

namespace Identity.Application.Features.Users;

/// <summary>
/// Lignes d'audit des actions sur les comptes (phase 6, tour 7b) : une par action réussie, dans la transaction de la commande.
/// Seuls les champs qui ont changé sont gardés. Jamais de mot de passe (ni temporaire, ni haché) : seulement le fait.
/// </summary>
internal static class AccountAudit
{
    public const string Created = "Identity.Account.Created";
    public const string Blocked = "Identity.Account.Blocked";
    public const string Unblocked = "Identity.Account.Unblocked";
    public const string RoleChanged = "Identity.Account.RoleChanged";
    public const string AgencyChanged = "Identity.Account.AgencyChanged";
    public const string PasswordReset = "Identity.Account.PasswordReset";
    public const string ProfileUpdated = "Identity.Account.ProfileUpdated";

    /// <param name="agencyId">Agence de la ligne (filtre de lecture) ; celle du compte par défaut.</param>
    public static async Task<Result> RecordAsync(
        IAuditLog audit,
        string action,
        UserAccount account,
        Guid actorId,
        IEnumerable<AuditChange> changes,
        CancellationToken cancellationToken,
        Guid? agencyId = null)
    {
        var changed = changes.Where(c => !string.Equals(c.Before, c.After, StringComparison.Ordinal)).ToList();
        var recorded = await audit.RecordAsync(
            new AuditRecord(action, "UserAccount", account.Id, actorId, agencyId ?? account.AgencyId, Changes: changed),
            cancellationToken);

        return recorded.IsFailure ? recorded.Error : Result.Success();
    }

    public static AuditChange Change(string field, object? before, object? after) =>
        new(field, before?.ToString(), after?.ToString());
}
