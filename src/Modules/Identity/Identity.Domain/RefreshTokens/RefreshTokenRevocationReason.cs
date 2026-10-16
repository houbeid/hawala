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
}