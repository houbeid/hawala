namespace Identity.Contracts;

/// <summary>
/// Le compte a-t-il cette permission ? Vérifié côté serveur, jamais lu dans le JWT (option B).
/// Un compte bloqué ou introuvable n'a aucune permission.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(Guid accountId, string permission, CancellationToken cancellationToken);
}