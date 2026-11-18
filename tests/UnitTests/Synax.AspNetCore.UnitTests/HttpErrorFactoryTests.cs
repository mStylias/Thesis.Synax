using Synax.AspNetCore.Errors;
using Synax.Results;

namespace Synax.AspNetCore.UnitTests;

public class HttpErrorFactoryTests
{
    [Test]
    public async Task NotFound_WithNoArguments_ShouldUseDefaults()
    {
        var error = HttpError.NotFound();

        await Assert.That(error.ErrorCode).IsEqualTo("NOT_FOUND");
        await Assert.That(error.HttpCode).IsEqualTo(404);
        await Assert.That(error.Description).IsNull();
        await Assert.That(error.ValidationErrors).IsNull();
    }

    [Test]
    public async Task NotFound_WithDescriptionAndErrorCode_ShouldOverrideBoth()
    {
        var error = HttpError.NotFound("Restaurant was not found.", errorCode: "RESTAURANT_NOT_FOUND");

        await Assert.That(error.ErrorCode).IsEqualTo("RESTAURANT_NOT_FOUND");
        await Assert.That(error.HttpCode).IsEqualTo(404);
        await Assert.That(error.Description).IsEqualTo("Restaurant was not found.");
    }

    [Test]
    public async Task Factories_ShouldUseConventionalCodesAndStatuses()
    {
        (HttpError Error, string Code, int Status)[] cases =
        [
            (HttpError.BadRequest(), "BAD_REQUEST", 400),
            (HttpError.Unauthorized(), "UNAUTHORIZED", 401),
            (HttpError.Forbidden(), "FORBIDDEN", 403),
            (HttpError.NotFound(), "NOT_FOUND", 404),
            (HttpError.Conflict(), "CONFLICT", 409),
            (HttpError.UnprocessableEntity(), "UNPROCESSABLE_ENTITY", 422),
            (HttpError.Internal(), "INTERNAL_ERROR", 500),
            (HttpError.Validation(), "VALIDATION_FAILED", 400)
        ];

        foreach (var (error, code, status) in cases)
        {
            await Assert.That(error.ErrorCode).IsEqualTo(code);
            await Assert.That(error.HttpCode).IsEqualTo(status);
        }
    }

    [Test]
    public async Task Validation_WithErrors_ShouldPopulateValidationErrors()
    {
        var error = HttpError.Validation(
            "Request is invalid.",
            validationErrors: [new ValidationError("Required", "Name is required")]);

        await Assert.That(error.HttpCode).IsEqualTo(400);
        await Assert.That(error.Description).IsEqualTo("Request is invalid.");
        await Assert.That(error.ValidationErrors).IsNotNull();
        await Assert.That(error.ValidationErrors!.Count).IsEqualTo(1);
        await Assert.That(error.ValidationErrors!.Single().Code).IsEqualTo("Required");
    }

    [Test]
    public async Task Validation_WithoutErrors_ShouldLeaveValidationErrorsNull()
    {
        var error = HttpError.Validation("Invalid.");

        await Assert.That(error.ValidationErrors).IsNull();
    }

    // -------- Origin stamping --------

    [Test]
    public async Task NotFound_ShouldStampResultOriginWithCallSite()
    {
        var error = HttpError.NotFound("x");

        await Assert.That(error.ResultOrigin).IsNotNull();
        await Assert.That(error.ResultOrigin!.MemberName)
            .IsEqualTo(nameof(NotFound_ShouldStampResultOriginWithCallSite));
        await Assert.That(error.ResultOrigin.FilePath).Contains(nameof(HttpErrorFactoryTests));
        await Assert.That(error.ResultOrigin.LineNumber).IsGreaterThan(0);
    }

    [Test]
    public async Task Factories_ShouldAllStampResultOrigin()
    {
        HttpError[] errors =
        [
            HttpError.BadRequest(), HttpError.Unauthorized(), HttpError.Forbidden(),
            HttpError.NotFound(), HttpError.Conflict(), HttpError.UnprocessableEntity(),
            HttpError.Internal(), HttpError.Validation()
        ];

        foreach (var error in errors)
        {
            await Assert.That(error.ResultOrigin).IsNotNull();
            await Assert.That(error.ResultOrigin!.MemberName)
                .IsEqualTo(nameof(Factories_ShouldAllStampResultOrigin));
        }
    }

    [Test]
    public async Task ImplicitConversion_ToFailedResult_ShouldTransferFactoryOrigin()
    {
        var error = HttpError.NotFound("x");

        Result<string, HttpError> result = error;

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsSameReferenceAs(error);
        await Assert.That(result.Origin).IsEqualTo(error.ResultOrigin);
    }
}
