using Synax.Results.UnitTests.TestModels;

namespace Synax.Results.UnitTests;

public class ResultOfTErrorTests
{
    [Test]
    public async Task Ok_WhenCalled_ShouldCreateSuccessResult()
    {
        // Arrange & Act
        Result<AppError> result = Result.Ok();

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Warn_WhenCalled_ShouldCreateWarningResult()
    {
        // Arrange & Act
        Result<AppError> result = Result.Warn();

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Fail_WhenCalledWithError_ShouldCreateErrorResult()
    {
        // Arrange
        var error = new AppError { Code = "NOT_FOUND", Message = "Item not found" };

        // Act
        Result<AppError> result = Result.Fail(error);

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Code).IsEqualTo("NOT_FOUND");
    }

    [Test]
    public async Task Fail_WhenCalled_ShouldCaptureOrigin()
    {
        // Arrange & Act
        Result<AppError> result = Result.Fail(new AppError { Code = "ERR" });

        // Assert
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName).IsEqualTo(nameof(Fail_WhenCalled_ShouldCaptureOrigin));
    }

    [Test]
    public async Task Fail_WhenCalledWithException_ShouldPropagateException()
    {
        // Arrange
        var exception = new InvalidOperationException("db error");
        var error = new AppError { Code = "DB_ERROR", Message = "Database failed" };

        // Act
        Result<AppError> result = Result.Fail(error)
            .WithException(exception);

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Exception).IsEqualTo(exception);
        await Assert.That(result.Error!.Code).IsEqualTo("DB_ERROR");
    }

    [Test]
    public async Task AddMessage_WhenCalled_ShouldReturnSameTypedResult()
    {
        // Arrange
        Result<AppError> result = Result.Fail(new AppError { Code = "ERR" });

        // Act
        var returned = result.AddMessage("layer info");

        // Assert
        await Assert.That(ReferenceEquals(result, returned)).IsTrue();
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("layer info");
    }

    [Test]
    public async Task Fail_WhenCalledWithStringError_ShouldTreatStringAsErrorNotCallerInfo()
    {
        // Arrange & Act
        Result<string> result = Result.Fail("User not found");

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsEqualTo("User not found");
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName)
            .IsEqualTo(nameof(Fail_WhenCalledWithStringError_ShouldTreatStringAsErrorNotCallerInfo));
    }

    [Test]
    public async Task Ok_WhenCalled_ShouldHaveNoOriginOrException()
    {
        // Arrange & Act
        Result<AppError> result = Result.Ok();

        // Assert
        await Assert.That(result.Origin).IsNull();
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Messages.Count).IsEqualTo(0);
    }
}
