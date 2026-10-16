using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Tests.Results;

public class ResultTests
{
    private static readonly Error SampleError = Error.Validation("Test.Invalid", "Valeur invalide");

    // --- Result sans valeur ------------------------------------------------

    [Fact]
    public void Success_is_success_and_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Error_converts_implicitly_to_failed_result()
    {
        Result result = SampleError;

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    // --- Result<T> ---------------------------------------------------------

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Generic_failure_has_no_value()
    {
        var result = Result.Failure<string>(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_with_null_value_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public void Value_converts_implicitly_to_successful_result()
    {
        Result<string> result = "jeton";

        Assert.True(result.IsSuccess);
        Assert.Equal("jeton", result.Value);
    }

    [Fact]
    public void Error_converts_implicitly_to_failed_generic_result()
    {
        Result<string> result = SampleError;

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }
}