using Hawala.SharedKernel.Results;

namespace Organization.Domain;

/// <summary>Erreurs métier du module Organisation. Codes stables, traduits par le frontend.</summary>
public static class OrganizationErrors
{
    public static class Agency
    {
        public static readonly Error CodeInvalid =
            Error.Validation("Organization.Agency.CodeInvalid", "Le code d'agence doit faire 3 lettres majuscules (ex. NKC).");

        public static readonly Error NameEmpty =
            Error.Validation("Organization.Agency.NameEmpty", "Le nom de l'agence est obligatoire.");

        public static readonly Error NameTooLong =
            Error.Validation("Organization.Agency.NameTooLong", "Le nom de l'agence dépasse 50 caractères.");

        public static readonly Error TimeZoneEmpty =
            Error.Validation("Organization.Agency.TimeZoneEmpty", "Le fuseau horaire de l'agence est obligatoire.");

        public static readonly Error NotFound =
            Error.NotFound("Organization.Agency.NotFound", "Agence introuvable.");
    }

    public static class Color
    {
        public static readonly Error Invalid =
            Error.Validation("Organization.Color.Invalid", "La couleur doit être au format #RRGGBB (ex. #1E8E3E).");
    }
}