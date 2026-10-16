using Identity.Domain.Roles;

namespace Identity.Domain.Accounts;

/// <summary>
/// Règle validée : on ne peut ni bloquer ni rétrograder le dernier Superviseur actif,
/// sinon plus personne ne peut gérer les comptes.
/// </summary>
public static class SupervisorGuard
{
    /// <param name="account">Compte qu'on s'apprête à bloquer ou à rétrograder.</param>
    /// <param name="activeSupervisorCount">
    /// Nombre de Superviseurs actifs en base, ce compte compris. L'Application le lit
    /// dans la même transaction, avec un verrou, pour éviter que deux Superviseurs se bloquent en même temps.
    /// </param>
    public static bool WouldRemoveLastActiveSupervisor(UserAccount account, int activeSupervisorCount)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegative(activeSupervisorCount);

        var isActiveSupervisor = account.Role == RoleTemplate.Supervisor && account.Status == AccountStatus.Active;

        return isActiveSupervisor && activeSupervisorCount <= 1;
    }
}