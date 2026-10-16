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

    public static class Account
    {
        public static readonly Error FullNameEmpty =
            Error.Validation("Identity.Account.FullNameEmpty", "Le nom complet est obligatoire.");

        public static readonly Error FullNameTooLong =
            Error.Validation("Identity.Account.FullNameTooLong", "Le nom complet dépasse 100 caractères.");

        public static readonly Error AlreadyBlocked =
            Error.Conflict("Identity.Account.AlreadyBlocked", "Le compte est déjà bloqué.");

        public static readonly Error NotBlocked =
            Error.Conflict("Identity.Account.NotBlocked", "Le compte n'est pas bloqué.");

        public static readonly Error LastActiveSupervisor =
            Error.Conflict(
                "Identity.Account.LastActiveSupervisor",
                "Impossible : c'est le dernier Superviseur actif. Nommer ou débloquer un autre Superviseur d'abord.");
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