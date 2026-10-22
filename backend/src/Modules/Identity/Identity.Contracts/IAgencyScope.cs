namespace Identity.Contracts;

/// <summary>
/// Périmètre du compte connecté (2e couche de sécurité) : quelle(s) agence(s) il a le droit de voir.
/// Un agent ne voit que son agence ; le Superviseur voit les deux. Rempli une fois par requête,
/// quand la session est validée ; utilisé par les filtres globaux EF Core des modules.
/// Sans session validée (appel anonyme, tâche de fond), il ne voit AUCUNE agence : un oubli
/// donne une liste vide, jamais les données de l'autre agence.
/// </summary>
public interface IAgencyScope
{
    /// <summary>Vrai pour un Superviseur.</summary>
    bool SeesAllAgencies { get; }

    /// <summary>Agence de l'agent ; null pour un Superviseur ou sans session validée.</summary>
    Guid? AgencyId { get; }

    bool CanSee(Guid agencyId);
}
