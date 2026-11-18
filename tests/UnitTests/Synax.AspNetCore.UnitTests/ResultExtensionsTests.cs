using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Errors;
using Synax.AspNetCore.Results;
using Synax.Results;

namespace Synax.AspNetCore.UnitTests;

public class ResultExtensionsTests
{
    [Test]
    public async Task ToHttpResult_WhenSuccessWithData_ShouldReturn200()
    {
        Result<string, HttpError> result = Result.Ok("hello");

        var httpResult = result.ToHttpResult();

        var okResult = httpResult.Result as Ok<string>;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.Value).IsEqualTo("hello");
    }

    [Test]
    public async Task ToHttpResult_WhenSuccessWithNullData_ShouldReturn204()
    {
        // Simulate a success result where TData is nullable and Data is null
        Result<object?, HttpError> result = Result.Ok<object?>(null);

        var httpResult = result.ToHttpResult();

        await Assert.That(httpResult.Result).IsTypeOf<NoContent>();
    }

    [Test]
    public async Task ToHttpResult_WhenError_ShouldReturnProblemDetails()
    {
        var error = new HttpError
        {
            ErrorCode = "NotFound",
            HttpCode = StatusCodes.Status404NotFound,
            Description = "User not found"
        };
        Result<string, HttpError> result = Result.Fail(error);

        var httpResult = result.ToHttpResult();

        // Problem Details result is a ProblemHttpResult
        var problemResult = httpResult.Result as ProblemHttpResult;
        await Assert.That(problemResult).IsNotNull();
        await Assert.That(problemResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(problemResult.ProblemDetails.Title).IsEqualTo("NotFound");
        await Assert.That(problemResult.ProblemDetails.Detail).IsEqualTo("User not found");
    }

    [Test]
    public async Task ToHttpResult_WhenErrorWithValidation_ShouldIncludeErrorsExtension()
    {
        var error = new HttpError
        {
            ErrorCode = "ValidationFailed",
            HttpCode = StatusCodes.Status400BadRequest,
            Description = "Validation failed",
            ValidationErrors = [new ValidationError("Required", "Name is required")]
        };
        Result<string, HttpError> result = Result.Fail(error);

        var httpResult = result.ToHttpResult();

        var problemResult = httpResult.Result as ProblemHttpResult;
        await Assert.That(problemResult).IsNotNull();
        await Assert.That(problemResult!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(problemResult.ProblemDetails.Extensions.ContainsKey("errors")).IsTrue();
    }

    [Test]
    public async Task ToHttpResult_WhenWarningWithData_ShouldReturn200()
    {
        Result<int, HttpError> result = Result.Warn(42);

        var httpResult = result.ToHttpResult();

        var okResult = httpResult.Result as Ok<int>;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.Value).IsEqualTo(42);
    }

    [Test]
    public async Task ToHttpResult_WhenError500_ShouldReturnProblem500()
    {
        var error = new HttpError
        {
            ErrorCode = "InternalServerError",
            HttpCode = StatusCodes.Status500InternalServerError,
            Description = "Something broke"
        };
        Result<string, HttpError> result = Result.Fail(error);

        var httpResult = result.ToHttpResult();

        var problemResult = httpResult.Result as ProblemHttpResult;
        await Assert.That(problemResult).IsNotNull();
        await Assert.That(problemResult!.StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
    }

    // -------- Custom success shape --------

    [Test]
    public async Task ToHttpResult_WithSuccessFactory_WhenSuccess_ShouldUseCustomShape()
    {
        Result<string, HttpError> result = Result.Ok("abc");

        var httpResult = result.ToHttpResult(data => TypedResults.Created($"/items/{data}", data));

        var created = httpResult.Result as Created<string>;
        await Assert.That(created).IsNotNull();
        await Assert.That(created!.Location).IsEqualTo("/items/abc");
        await Assert.That(created.Value).IsEqualTo("abc");
    }

    [Test]
    public async Task ToHttpResult_WithSuccessFactory_WhenSuccessWithNullData_ShouldReturn204WithoutInvokingFactory()
    {
        var invoked = false;
        Result<object?, HttpError> result = Result.Ok<object?>(null);

        var httpResult = result.ToHttpResult(data =>
        {
            invoked = true;
            return TypedResults.Created($"/items/{data}", data);
        });

        await Assert.That(invoked).IsFalse();
        await Assert.That(httpResult.Result).IsTypeOf<NoContent>();
    }

    [Test]
    public async Task ToHttpResult_WithSuccessFactory_WhenError_ShouldReturnProblemDetailsWithoutInvokingFactory()
    {
        var invoked = false;
        var error = new HttpError
        {
            ErrorCode = "Conflict",
            HttpCode = StatusCodes.Status409Conflict,
            Description = "Already exists"
        };
        Result<string, HttpError> result = Result.Fail(error);

        var httpResult = result.ToHttpResult(data =>
        {
            invoked = true;
            return TypedResults.Created($"/items/{data}", data);
        });

        await Assert.That(invoked).IsFalse();
        var problemResult = httpResult.Result as ProblemHttpResult;
        await Assert.That(problemResult).IsNotNull();
        await Assert.That(problemResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(problemResult.ProblemDetails.Title).IsEqualTo("Conflict");
    }

    // -------- Void result --------

    [Test]
    public async Task VoidToHttpResult_WhenSuccess_ShouldReturn204()
    {
        Result<HttpError> result = Result.Ok();

        var httpResult = result.ToHttpResult();

        await Assert.That(httpResult.Result).IsTypeOf<NoContent>();
    }

    [Test]
    public async Task VoidToHttpResult_WhenError_ShouldReturnProblemDetails()
    {
        Result<HttpError> result = Result.Fail(new HttpError
        {
            ErrorCode = "Forbidden",
            HttpCode = StatusCodes.Status403Forbidden
        });

        var httpResult = result.ToHttpResult();

        var problemResult = httpResult.Result as ProblemHttpResult;
        await Assert.That(problemResult).IsNotNull();
        await Assert.That(problemResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }
}
