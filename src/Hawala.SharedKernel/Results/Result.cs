namespace Hawala.SharedKernel.Results;

/// <summary>
/// Résultat d'une opération sans valeur de retour : succès, ou échec avec une <see cref="Results.Error"/>.
/// Utilisé pour les erreurs métier attendues ; les pannes restent des exceptions.
/// </summary>
public class Result
{
    private readonly Error? _error;

    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new ArgumentException("Un succès ne peut pas porter d'erreur.", nameof(error));
        }

        if (!isSuccess && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Un échec doit porter une erreur.");
        }

        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>Erreur de l'échec. Lire l'erreur d'un succès est un bug : exception.</summary>
    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Un résultat en succès n'a pas d'erreur.");

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(Error error) => new(error);

    /// <summary>Permet d'écrire <c>return IdentityErrors.AccountBlocked;</c> dans un handler.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Résultat d'une opération qui renvoie une valeur en cas de succès.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value)
        : base(true, null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "Un succès doit porter une valeur.");
        }

        _value = value;
    }

    internal Result(Error error)
        : base(false, error)
    {
        _value = default;
    }

    /// <summary>Valeur du succès. Lire la valeur d'un échec est un bug : exception.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Un résultat en échec n'a pas de valeur ({Error.Code}).");

    /// <summary>Permet d'écrire <c>return token;</c> dans un handler qui renvoie Result&lt;Token&gt;.</summary>
    public static implicit operator Result<T>(T value) => new(value);

    /// <summary>Permet d'écrire <c>return IdentityErrors.InvalidCredentials;</c>.</summary>
    public static implicit operator Result<T>(Error error) => new(error);
}