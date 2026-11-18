using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Errors;

namespace Synax.AspNetCore.UnitTests;

public class HttpErrorExtensionsTests
{
    [Test]
    public async Task ToProblemResult_ShouldMapStatusCorrectly()
    {
        var error = new HttpError { HttpCode = 404, ErrorCode = "NotFound", Description = "Not found" };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task ToProblemResult_ShouldMapTitleFromErrorCode()
    {
        var error = new HttpError { HttpCode = 400, ErrorCode = "BadRequest" };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ProblemDetails.Title).IsEqualTo("BadRequest");
    }

    [Test]
    public async Task ToProblemResult_ShouldMapDetailFromDescription()
    {
        var error = new HttpError
        {
            HttpCode = 500,
            ErrorCode = "InternalServerError",
            Description = "Something went wrong"
        };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ProblemDetails.Detail).IsEqualTo("Something went wrong");
    }

    [Test]
    public async Task ToProblemResult_WhenValidationErrors_ShouldIncludeInExtensions()
    {
        var error = new HttpError
        {
            HttpCode = 400,
            ErrorCode = "ValidationFailed",
            ValidationErrors =
            [
                new ValidationError("Required", "Name is required")
            ]
        };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ProblemDetails.Extensions.ContainsKey("errors")).IsTrue();
    }

    [Test]
    public async Task ToProblemResult_WhenNoValidationErrors_ShouldNotIncludeErrorsExtension()
    {
        var error = new HttpError { HttpCode = 500, ErrorCode = "Error" };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ProblemDetails.Extensions.ContainsKey("errors")).IsFalse();
    }

    [Test]
    public async Task ToProblemResult_WhenHttpCodeUnset_ShouldFallBackTo500()
    {
        var error = new HttpError { ErrorCode = "Forgotten" };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(500);
    }

    [Test]
    public async Task ToProblemResult_WhenNullDescription_ShouldSetNullDetail()
    {
        var error = new HttpError { HttpCode = 500, ErrorCode = "Error", Description = null };

        var result = error.ToProblemResult() as ProblemHttpResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ProblemDetails.Detail).IsNull();
    }

    // -------- PropertyName camelCasing --------

    [Test]
    public async Task ToProblemResult_ShouldCamelCasePropertyName()
    {
        var errors = ErrorsExtensionOf(new ValidationError("PARTY_TOO_LARGE", "Max is 8.", "PartySize"));

        await Assert.That(errors[0].PropertyName).IsEqualTo("partySize");
        await Assert.That(errors[0].Code).IsEqualTo("PARTY_TOO_LARGE");
        await Assert.That(errors[0].Description).IsEqualTo("Max is 8.");
    }

    [Test]
    public async Task ToProblemResult_ShouldCamelCaseEachDottedPathSegment()
    {
        var errors = ErrorsExtensionOf(new ValidationError("REQUIRED", "Required.", "Address.Street"));

        await Assert.That(errors[0].PropertyName).IsEqualTo("address.street");
    }

    [Test]
    public async Task ToProblemResult_ShouldPreserveIndexerSegments()
    {
        var errors = ErrorsExtensionOf(new ValidationError("REQUIRED", "Required.", "Items[2].Name"));

        await Assert.That(errors[0].PropertyName).IsEqualTo("items[2].name");
    }

    [Test]
    public async Task ToProblemResult_WhenNullPropertyName_ShouldStayNull()
    {
        var errors = ErrorsExtensionOf(new ValidationError("GENERAL", "Cross-field failure."));

        await Assert.That(errors[0].PropertyName).IsNull();
    }

    [Test]
    public async Task ToProblemResult_ShouldCamelCaseInnerErrorsRecursively()
    {
        var outer = new ValidationError("ADDRESS_INVALID", "Address is invalid.", "Address",
            [new ValidationError("REQUIRED", "Street is required.", "Street")]);

        var errors = ErrorsExtensionOf(outer);

        await Assert.That(errors[0].PropertyName).IsEqualTo("address");
        await Assert.That(errors[0].InnerErrors).IsNotNull();
        await Assert.That(errors[0].InnerErrors!.Single().PropertyName).IsEqualTo("street");
        await Assert.That(errors[0].InnerErrors!.Single().Code).IsEqualTo("REQUIRED");
    }

    // -------- JSON shape --------

    [Test]
    public async Task Serialization_ShouldOmitNullPropertyNameAndInnerErrors()
    {
        var webOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        var withoutProperty = System.Text.Json.JsonSerializer.Serialize(
            new ValidationError("REQUIRED", "Name is required"), webOptions);
        var withProperty = System.Text.Json.JsonSerializer.Serialize(
            new ValidationError("REQUIRED", "Name is required", "name"), webOptions);

        await Assert.That(withoutProperty).DoesNotContain("propertyName");
        await Assert.That(withoutProperty).DoesNotContain("innerErrors");
        await Assert.That(withProperty).Contains("\"propertyName\":\"name\"");
    }

    private static List<ValidationError> ErrorsExtensionOf(params ValidationError[] validationErrors)
    {
        var error = new HttpError
        {
            HttpCode = 400,
            ErrorCode = "VALIDATION_FAILED",
            ValidationErrors = validationErrors
        };

        var result = (ProblemHttpResult)error.ToProblemResult();
        return (List<ValidationError>)result.ProblemDetails.Extensions["errors"]!;
    }
}
