using Identity.Domain.Accounts;
using Organization.Contracts;

namespace Identity.Application.Features.Users;

/// <summary>Un compte tel que le Superviseur le voit dans la liste et la fiche des comptes.</summary>
/// <param name="Role">Supervisor, Agent, Cashier ou ReadOnly.</param>
/// <param name="Status">Active ou Blocked.</param>
/// <param name="Agency">Agence de l'agent ; null pour un Superviseur, qui voit les deux.</param>
/// <param name="LockedUntil">Fin du verrouillage après 5 échecs ; null si le compte n'est pas verrouillé.</param>
public sealed record UserSummary(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string Role,
    UserAgency? Agency,
    string Status,
    bool MustChangePassword,
    DateTimeOffset? LockedUntil);

/// <summary>Résumé de l'agence d'un compte, pour le badge et la couleur.</summary>
public sealed record UserAgency(Guid Id, string Code, string Name, string Color);

internal static class UserSummaryMapper
{
    /// <param name="agencies">Agences connues, par identifiant (lues une seule fois pour toute la liste).</param>
    /// <param name="lockoutEnds">Comptes encore verrouillés et fin de leur verrouillage.</param>
    public static UserSummary ToSummary(
        UserAccount account,
        IReadOnlyDictionary<Guid, AgencySummary> agencies,
        IReadOnlyDictionary<Guid, DateTimeOffset> lockoutEnds)
    {
        UserAgency? agency = null;
        if (account.AgencyId is { } agencyId && agencies.TryGetValue(agencyId, out var summary))
        {
            agency = new UserAgency(summary.Id, summary.Code, summary.Name, summary.Color);
        }

        return new UserSummary(
            account.Id,
            account.FullName,
            account.Email.Value,
            account.Phone?.Value,
            account.Role.ToString(),
            agency,
            account.Status.ToString(),
            account.MustChangePassword,
            lockoutEnds.TryGetValue(account.Id, out var lockedUntil) ? lockedUntil : null);
    }
}
