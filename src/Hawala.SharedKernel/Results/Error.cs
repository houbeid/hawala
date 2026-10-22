namespace Hawala.SharedKernel.Results;

/// <summary>
/// Nature d'une erreur métier. L'API s'en sert pour choisir le code HTTP
/// (Validation → 400, NotFound → 404, Conflict → 409…) ; le Domain et
/// l'Application ne connaissent jamais HTTP.
/// </summary>
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <summary>
/// Erreur métier attendue (identifiants invalides, compte bloqué, transfert déjà livré…).
/// Les erreurs inattendues restent des exceptions.
/// </summary>
/// <param name="Code">
/// Code stable, en anglais, au format "Module.Raison" (ex. "Identity.InvalidCredentials").
/// Le frontend traduit ce code en français ou en arabe : il ne doit jamais changer
/// une fois publié.
/// </param>
/// <param name="Message">Description pour les logs et les développeurs, jamais affichée telle quelle.</param>
/// <param name="Type">Nature de l'erreur.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// Valeurs utiles au client pour un refus précis (ex. « remaining » : montant encore possible sous un plafond),
    /// ajoutées à la réponse par l'API. Null pour la plupart des erreurs. Se pose avec
    /// <c>Erreur.Connue with { Details = … }</c> : l'erreur publiée, elle, reste sans détail.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}