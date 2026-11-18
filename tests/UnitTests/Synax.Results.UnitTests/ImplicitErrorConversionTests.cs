namespace Synax.Results.UnitTests;

public class ImplicitErrorConversionTests
{
    private sealed record PlainError(string Message);

    private sealed record OriginError(string Message) : IHasResultOrigin
    {
        public ResultOrigin? ResultOrigin { get; init; }
    }

    [Test]
    public async Task ImplicitConversion_ToDataErrorResult_ShouldProduceFailureWithSameErrorInstance()
    {
        var error = new PlainError("not found");

        Result<string, PlainError> result = error;

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsSameReferenceAs(error);
        await Assert.That(result.Messages.Count).IsEqualTo(0);
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task ImplicitConversion_ToErrorResult_ShouldProduceFailureWithSameErrorInstance()
    {
        var error = new PlainError("invalid");

        Result<PlainError> result = error;

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsSameReferenceAs(error);
    }

    [Test]
    public async Task ImplicitConversion_WhenErrorHasNoOrigin_ShouldLeaveOriginNull()
    {
        Result<string, PlainError> result = new PlainError("x");

        await Assert.That(result.Origin).IsNull();
    }

    [Test]
    public async Task ImplicitConversion_WhenErrorImplementsIHasResultOrigin_ShouldTransferOrigin()
    {
        var origin = new ResultOrigin("SomeMethod", "/src/SomeFile.cs", 42);
        var error = new OriginError("x") { ResultOrigin = origin };

        Result<string, OriginError> dataError = error;
        Result<OriginError> voidError = error;

        await Assert.That(dataError.Origin).IsEqualTo(origin);
        await Assert.That(voidError.Origin).IsEqualTo(origin);
    }

    [Test]
    public async Task ImplicitConversion_WhenErrorIsNull_ShouldThrowArgumentNullException()
    {
        await Assert.That(() =>
        {
            Result<string, PlainError> _ = (PlainError)null!;
        }).Throws<ArgumentNullException>();
    }

    // -------- FromError overloads --------

    [Test]
    public async Task FromError_WithUntypedSourceAndExplicitError_ShouldPreserveOriginExceptionAndMessages()
    {
        var exception = new InvalidOperationException("boom");
        Result<int, PlainError> source = Result
            .Fail(new PlainError("original"))
            .WithException(exception)
            .WithMessage("row 42 was invalid");
        var replacement = new PlainError("replacement");

        var converted = Result<string, PlainError>.FromError(source, replacement);

        await Assert.That(converted.IsError).IsTrue();
        await Assert.That(converted.Error).IsSameReferenceAs(replacement);
        await Assert.That(converted.Origin).IsEqualTo(source.Origin);
        await Assert.That(converted.Exception).IsSameReferenceAs(exception);
        await Assert.That(converted.Messages).Contains("row 42 was invalid");
    }

    [Test]
    public async Task FromError_WithOtherDataShape_ShouldPreserveErrorOriginExceptionAndMessages()
    {
        var exception = new InvalidOperationException("boom");
        Result<int, PlainError> source = Result
            .Fail(new PlainError("original"))
            .WithException(exception)
            .WithMessage("context");

        var converted = Result<string, PlainError>.FromError(source);

        await Assert.That(converted.IsError).IsTrue();
        await Assert.That(converted.Error).IsSameReferenceAs(source.Error);
        await Assert.That(converted.Origin).IsEqualTo(source.Origin);
        await Assert.That(converted.Exception).IsSameReferenceAs(exception);
        await Assert.That(converted.Messages).Contains("context");
    }

    [Test]
    public async Task FromError_WhenSourceIsNotAnError_ShouldThrowInvalidOperationException()
    {
        Result<int, PlainError> success = Result.Ok<int, PlainError>(1);

        await Assert.That(() => Result<string, PlainError>.FromError(success, new PlainError("x")))
            .Throws<InvalidOperationException>();
        await Assert.That(() => Result<string, PlainError>.FromError(success))
            .Throws<InvalidOperationException>();
    }
}
