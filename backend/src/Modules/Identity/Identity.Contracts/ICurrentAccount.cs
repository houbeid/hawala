namespace Identity.Contracts;

/// <summary>
/// Compte connecté pour la requête en cours, rempli avec le périmètre quand la session est validée.
/// Null sans session validée (appel anonyme, tâche de fond, test qui appelle un service directement).
/// </summary>
public interface ICurrentAccount
{
    Guid? AccountId { get; }
}
