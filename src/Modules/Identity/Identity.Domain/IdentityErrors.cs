using Hawala.SharedKernel.Results;

namespace Identity.Domain;

/// <summary>
/// Toutes les erreurs métier du module Identité.
/// Les codes sont stables : le frontend les traduit (fr / ar). Ne jamais renommer un code publié.
/// </summary>
public static class IdentityErrors
{
    public static class Email
    {
        public static readonly Error Empty =
            Error.Validation("Identity.Email.Empty", "L'email est obligatoire.");

        public static readonly Error TooLong =
            Error.Validation("Identity.Email.TooLong", "L'email dépasse 254 caractères.");

        public static readonly Error Invalid =
            Error.Validation("Identity.Email.Invalid", "Le format de l'email est invalide.");
    }

    public static class Phone
    {
        public static readonly Error Empty =
            Error.Validation("Identity.Phone.Empty", "Le numéro de téléphone est vide.");

        public static readonly Error Invalid =
            Error.Validation(
                "Identity.Phone.Invalid",
                "Le numéro doit être au format international, ex. +222 22 12 34 56 ou +221 77 123 45 67.");
    }

    public static class Login
    {
        public static readonly Error Empty =
            Error.Validation("Identity.Login.Empty", "Saisir un email ou un numéro de téléphone.");
    }

    public static class Auth
    {
        // Même erreur que le compte existe ou non : on ne révèle jamais si un email ou un téléphone est inscrit.
        public static readonly Error InvalidCredentials =
            Error.Unauthorized("Identity.Auth.InvalidCredentials", "Identifiant ou mot de passe incorrect.");

        public static readonly Error LockedOut =
            Error.Unauthorized("Identity.Auth.LockedOut", "Trop d'essais : compte verrouillé temporairement.");

        public static readonly Error AccountBlocked =
            Error.Forbidden("Identity.Auth.AccountBlocked", "Ce compte est bloqué. Contacter le Superviseur.");
    }

    public static class Password
    {
        public static readonly Error Empty =
            Error.Validation("Identity.Password.Empty", "Le mot de passe est obligatoire.");

        public static readonly Error Incorrect =
            Error.Validation("Identity.Password.Incorrect", "Le mot de passe actuel est incorrect.");

        public static readonly Error TooWeak =
            Error.Validation("Identity.Password.TooWeak", "Le nouveau mot de passe ne respecte pas les règles.");
    }

    public static class Account
    {
        public static readonly Error NotFound =
            Error.NotFound("Identity.Account.NotFound", "Compte introuvable.");

        public static readonly Error FullNameEmpty =
            Error.Validation("Identity.Account.FullNameEmpty", "Le nom complet est obligatoire.");

        public static readonly Error FullNameTooLong =
            Error.Validation("Identity.Account.FullNameTooLong", "Le nom complet dépasse 100 caractères.");

        public static readonly Error AlreadyBlocked =
            Error.Conflict("Identity.Account.AlreadyBlocked", "Le compte est déjà bloqué.");

        public static readonly Error NotBlocked =
            Error.Conflict("Identity.Account.NotBlocked", "Le compte n'est pas bloqué.");

        public static readonly Error EmailTaken =
            Error.Conflict("Identity.Account.EmailTaken", "Cet email est déjà utilisé par un autre compte.");

        public static readonly Error PhoneTaken =
            Error.Conflict("Identity.Account.PhoneTaken", "Ce numéro est déjà utilisé par un autre compte.");

        public static readonly Error RoleInvalid =
            Error.Validation(
                "Identity.Account.RoleInvalid",
                "Rôle inconnu. Valeurs possibles : Supervisor, Agent, Cashier, ReadOnly.");

        public static readonly Error StatusInvalid =
            Error.Validation("Identity.Account.StatusInvalid", "Statut inconnu. Valeurs possibles : Active, Blocked.");

        public static readonly Error AgencyNotFound =
            Error.Validation("Identity.Account.AgencyNotFound", "Cette agence n'existe pas.");

        public static readonly Error AgencyRequired =
            Error.Validation("Identity.Account.AgencyRequired", "Un agent doit être rattaché à une agence.");

        public static readonly Error AgencyNotAllowedForSupervisor =
            Error.Validation(
                "Identity.Account.AgencyNotAllowedForSupervisor",
                "Un Superviseur n'est rattaché à aucune agence : il voit les deux.");

        public static readonly Error CannotBlockSelf =
            Error.Conflict(
                "Identity.Account.CannotBlockSelf",
                "Impossible de bloquer son propre compte. Demander à un autre Superviseur.");

        public static readonly Error CannotChangeOwnRole =
            Error.Conflict(
                "Identity.Account.CannotChangeOwnRole",
                "Impossible de changer son propre rôle. Demander à un autre Superviseur.");

        public static readonly Error CannotResetOwnPassword =
            Error.Conflict(
                "Identity.Account.CannotResetOwnPassword",
                "Impossible de réinitialiser son propre mot de passe. Utiliser le changement de mot de passe.");

        public static readonly Error SupervisorOnly =
            Error.Forbidden(
                "Identity.Account.SupervisorOnly",
                "Réservé au Superviseur : changer le rôle ou l'agence d'un compte, créer un compte Superviseur.");

        public static readonly Error OtherAgency =
            Error.Forbidden("Identity.Account.OtherAgency", "Un compte se crée dans son agence seulement.");

        public static readonly Error LastActiveSupervisor =
            Error.Conflict(
                "Identity.Account.LastActiveSupervisor",
                "Impossible : c'est le dernier Superviseur actif. Nommer ou débloquer un autre Superviseur d'abord.");
    }

    public static class Permissions
    {
        public static readonly Error Required =
            Error.Validation(
                "Identity.Permissions.Required",
                "Envoyer la liste complète des permissions voulues (vide = aucune).");

        public static readonly Error Unknown =
            Error.Validation("Identity.Permissions.Unknown", "Permission inconnue.");

        public static readonly Error SupervisorHasAll =
            Error.Conflict(
                "Identity.Permissions.SupervisorHasAll",
                "Un Superviseur a toujours toutes les permissions. Pour réduire ses droits, changer son rôle.");

        public static readonly Error SupervisorOnly =
            Error.Forbidden(
                "Identity.Permissions.SupervisorOnly",
                "Seul un Superviseur peut donner ou retirer des permissions.");
    }

    public static class Session
    {
        public static readonly Error NotFound =
            Error.NotFound("Identity.Session.NotFound", "Session introuvable ou déjà fermée.");
    }

    public static class Limits
    {
        public static readonly Error SupervisorHasNone =
            Error.Conflict("Identity.Limits.SupervisorHasNone", "Un Superviseur n'a jamais de plafond.");

        public static readonly Error Negative =
            Error.Validation("Identity.Limits.Negative", "Un plafond est positif ou nul (vide = pas de limite).");

        public static readonly Error TooPrecise =
            Error.Validation(
                "Identity.Limits.TooPrecise",
                "Plafond trop précis : FCFA en nombre entier, MRO avec 2 décimales au plus.");

        public static readonly Error AgencyRequired =
            Error.Conflict("Identity.Limits.AgencyRequired", "Un plafond se fixe dans la devise de l'agence du compte.");
    }

    // Côté client, ces quatre erreurs mènent au même résultat : retour à l'écran de connexion.
    // Les codes distincts servent aux logs et à l'audit (surtout Reused, signe de vol de jeton).
    public static class RefreshToken
    {
        public static readonly Error Invalid =
            Error.Unauthorized("Identity.RefreshToken.Invalid", "Jeton de rafraîchissement inconnu.");

        public static readonly Error Expired =
            Error.Unauthorized("Identity.RefreshToken.Expired", "Jeton de rafraîchissement expiré.");

        public static readonly Error Revoked =
            Error.Unauthorized("Identity.RefreshToken.Revoked", "Jeton de rafraîchissement révoqué.");

        public static readonly Error Reused =
            Error.Unauthorized(
                "Identity.RefreshToken.Reused",
                "Jeton déjà utilisé : possible vol de session, toute la famille de jetons doit être révoquée.");
    }
}
