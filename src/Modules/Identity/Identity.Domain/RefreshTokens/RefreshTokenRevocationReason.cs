namespace Identity.Domain.RefreshTokens;

/// <summary>Pourquoi un refresh token a été révoqué. Sert à la détection de vol et à l'audit.</summary>
public enum RefreshTokenRevocationReason
{
    /// <summary>Remplacé par un nouveau jeton lors d'un rafraîchissement normal.</summary>
    Rotated,

    /// <summary>L'agent s'est déconnecté.</summary>
    Logout,

    /// <summary>Un jeton déjà remplacé a été rejoué : toute la famille est révoquée.</summary>
    ReuseDetected,

    /// <summary>Le Superviseur a bloqué le compte.</summary>
    AccountBlocked,

    /// <summary>Le mot de passe a changé : les autres sessions sont fermées.</summary>
    PasswordChanged,

    /// <summary>
    /// Le Superviseur a donné un nouveau mot de passe temporaire (réinitialisation ou déblocage) :
    /// toutes les sessions du compte sont fermées.
    /// Nom court volontaire : la colonne est limitée à 20 caractères.
    /// </summary>
    PasswordReset,

    /// <summary>La session a été fermée depuis la liste des appareils (par son propriétaire ou par celui qui gère le compte).</summary>
    SessionClosed,
}