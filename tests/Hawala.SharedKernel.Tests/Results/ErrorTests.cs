using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Tests.Results;

public class ErrorTests
{
    [Fact]
    public void Each_factory_sets_its_error_type()
    {
        Assert.Equal(ErrorType.Failure, Error.Failure("T.Code", "m").Type);
        Assert.Equal(ErrorType.Validation, Error.Validation("T.Code", "m").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("T.Code", "m").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("T.Code", "m").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("T.Code", "m").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("T.Code", "m").Type);
    }

    [Fact]
    public void Two_errors_with_same_values_are_equal()
    {
        var a = Error.Conflict("Identity.EmailTaken", "Email déjà utilisé");
        var b = Error.Conflict("Identity.EmailTaken", "Email déjà utilisé");

        Assert.Equal(a, b);
    }
}