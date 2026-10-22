using Identity.Contracts;
using Identity.Domain.Roles;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Implémentation de <see cref="IAgencyScope"/> et de <see cref="ICurrentAccount"/>, une par requête. Remplie par <see cref="SessionValidator"/>
/// au moment où il valide la session, à partir de l'état déjà mis en cache : aucune requête en base en plus.
/// Vide par défaut : sans session validée, aucune agence n'est visible.
/// </summary>
public sealed class AgencyScope : IAgencyScope, ICurrentAccount
{
    public Guid? AccountId { get; private set; }

    public bool SeesAllAgencies { get; private set; }

    public Guid? AgencyId { get; private set; }

    public bool CanSee(Guid agencyId) => SeesAllAgencies || AgencyId == agencyId;

    internal void Set(Guid accountId, RoleTemplate role, Guid? agencyId)
    {
        AccountId = accountId;

        // Le rôle décide, pas la présence d'une agence : un Superviseur n'en a jamais (règle du Domain).
        SeesAllAgencies = role == RoleTemplate.Supervisor;
        AgencyId = SeesAllAgencies ? null : agencyId;
    }
}
