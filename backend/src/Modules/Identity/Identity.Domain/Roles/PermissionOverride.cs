namespace Identity.Domain.Roles;

/// <summary>Sens d'un ajustement : permission ajoutée au modèle du rôle, ou retirée.</summary>
public enum PermissionOverrideKind
{
    Granted,
    Revoked,
}

/// <summary>
/// Ajustement d'un compte par rapport au modèle de son rôle (phase 6, règle validée le 21 octobre 2026) :
/// « Granted » = permission absente du modèle mais donnée à ce compte ; « Revoked » = permission du modèle retirée.
/// </summary>
public sealed record PermissionOverride(string Permission, PermissionOverrideKind Kind);
