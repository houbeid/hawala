namespace Hawala.SharedKernel.Ids;

/// <summary>
/// Génère les identifiants techniques de toutes les entités (UUID v7).
/// Un UUID v7 commence par l'horodatage : les identifiants sont triés par date de création,
/// ce qui garde les index PostgreSQL compacts (contrairement aux UUID v4 aléatoires).
/// Ces identifiants ne sont jamais affichés à l'utilisateur : il voit les références lisibles.
/// </summary>
public static class IdGenerator
{
    /// <summary>Nouvel identifiant, horodaté à l'instant courant.</summary>
    public static Guid NewId() => Guid.CreateVersion7();

    /// <summary>
    /// Nouvel identifiant horodaté à un instant donné, pour suivre l'horloge injectée
    /// (utile pour garder l'ordre des identifiants cohérent avec <see cref="Time.IClock"/> dans les tests).
    /// </summary>
    public static Guid NewId(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);
}