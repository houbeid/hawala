using Hawala.SharedKernel.Results;

namespace Identity.Application.Abstractions;

public enum PasswordCheckResult
{
    Success,
    Invalid,

    /// <summary>Trop d'échecs (5) : le compte est verrouillé pour un temps, même avec le bon mot de passe.</summary>
    LockedOut,
}

/// <summary>
/// Mots de passe et verrouillage après échecs. Implémenté avec ASP.NET Identity en Infrastructure :
/// l'Application ne voit jamais un hash de mot de passe.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Crée les identifiants d'un nouveau compte avec son mot de passe temporaire.</summary>
    Task<Result> CreateAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Vérifie le mot de passe. Un échec incrémente le compteur ; au 5e, le compte est verrouillé.
    /// Un succès remet le compteur à zéro.
    /// </summary>
    Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Fait le même travail qu'une vraie vérification, sur un compte inexistant, pour que la durée
    /// de la réponse ne révèle pas si un email ou un téléphone existe.
    /// </summary>
    Task SimulatePasswordCheckAsync(string password, CancellationToken cancellationToken);

    /// <summary>Change le mot de passe en vérifiant l'ancien et la politique de mot de passe.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);
}