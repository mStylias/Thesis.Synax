using Synax.Results.UnitTests.TestModels;

namespace Synax.Results.UnitTests;

public class ResultOfTDataTErrorTests
{
    [Test]
    public async Task ImplicitConversion_WhenReturningData_ShouldCreateSuccessResult()
    {
        // Arrange & Act
        Result<UserDto, AppError> result = new UserDto { Id = 1, Name = "Alice" };

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Data).IsNotNull();
        await Assert.That(result.Data!.Id).IsEqualTo(1);
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Fail_WhenCalledWithError_ShouldCreateErrorResult()
    {
        // Arrange & Act
        Result<UserDto, AppError> result = Result.Fail(new AppError { Code = "NOT_FOUND", Message = "User not found" });

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Code).IsEqualTo("NOT_FOUND");
        await Assert.That(result.Data).IsNull();
    }

    [Test]
    public async Task Ok_WhenCalledWithData_ShouldCreateSuccessResult()
    {
        // Arrange
        var user = new UserDto { Id = 5, Name = "Eve" };

        // Act
        Result<UserDto, AppError> result = Result.Ok(user);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsNotNull();
        await Assert.That(result.Data!.Id).IsEqualTo(5);
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Fail_WhenCalledWithTypedError_ShouldCreateErrorResult()
    {
        // Arrange
        var error = new AppError { Code = "INVALID", Message = "Bad request" };

        // Act
        Result<UserDto, AppError> result = Result.Fail(error);

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Code).IsEqualTo("INVALID");
        await Assert.That(result.Data).IsNull();
    }

    [Test]
    public async Task Warn_WhenCalledWithData_ShouldCreateWarningResult()
    {
        // Arrange
        var user = new UserDto { Id = 1, Name = "Warn user" };

        // Act
        Result<UserDto, AppError> result = Result.Warn(user);

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsNotNull();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task Fail_WhenCalledWithTypedError_ShouldCaptureOrigin()
    {
        // Arrange
        var error = new AppError { Code = "ERR", Message = "error" };

        // Act
        Result<UserDto, AppError> result = Result.Fail(error);

        // Assert
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName).IsEqualTo(nameof(Fail_WhenCalledWithTypedError_ShouldCaptureOrigin));
    }

    [Test]
    public async Task Fail_WhenTypedErrorWithExceptionAndMessage_ShouldPropagateToResult()
    {
        // Arrange
        var error = new AppError { Code = "PAYMENT", Message = "Declined" };
        var exception = new InvalidOperationException("Payment gateway error");

        // Act
        Result<UserDto, AppError> result = Result.Fail(error)
            .WithException(exception)
            .WithMessage("Payment processing failed");

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Error!.Code).IsEqualTo("PAYMENT");
        await Assert.That(result.Exception).IsEqualTo(exception);
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("Payment processing failed");
    }

    [Test]
    public async Task AddMessage_WhenCalled_ShouldReturnSameTypedResult()
    {
        // Arrange
        Result<UserDto, AppError> result = Result.Fail(new AppError { Code = "ERR", Message = "error" });

        // Act
        var returned = result.AddMessage("context");

        // Assert
        await Assert.That(ReferenceEquals(result, returned)).IsTrue();
        await Assert.That(result.Messages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Ok_WhenCalled_ShouldHaveNoOriginOrException()
    {
        // Arrange & Act
        Result<UserDto, AppError> result = Result.Ok(new UserDto { Id = 1, Name = "Test" });

        // Assert
        await Assert.That(result.Origin).IsNull();
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Messages.Count).IsEqualTo(0);
    }

}
