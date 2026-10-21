using Hawala.SharedKernel.Results;

namespace Hawala.Api.Errors;

/// <summary>
/// Seul endroit qui traduit une erreur métier en réponse HTTP (format ProblemDetails, RFC 9457).
/// Le frontend lit « code » pour afficher le message en français ou en arabe.
/// </summary>
public static class ErrorResults
{
    public static IResult ToProblem(this Error error) => error.ToProblem(extra: null);

    /// <param name="extra">Champs ajoutés à la réponse (ex. « remainingAttempts » pour un code de retrait faux).</param>
    public static IResult ToProblem(this Error error, IReadOnlyDictionary<string, object?>? extra)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return TypedResults.Problem(
            statusCode: status,
            title: error.Code,
            detail: error.Message,
            extensions: extensions);
    }
}