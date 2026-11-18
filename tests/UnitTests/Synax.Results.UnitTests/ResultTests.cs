namespace Synax.Results.UnitTests;

public class ResultTests
{
    [Test]
    public async Task Ok_WhenCalled_ShouldReturnSuccessResult()
    {
        // Arrange & Act
        Result result = Result.Ok();

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.IsWarning).IsFalse();
        await Assert.That(result.Status).IsEqualTo(ResultStatus.Success);
    }

    [Test]
    public async Task Fail_WhenCalled_ShouldReturnErrorResult()
    {
        // Arrange & Act
        Result result = Result.Fail();

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.IsWarning).IsFalse();
        await Assert.That(result.Status).IsEqualTo(ResultStatus.Error);
    }

    [Test]
    public async Task Warn_WhenCalled_ShouldReturnWarningResult()
    {
        // Arrange & Act
        Result result = Result.Warn();

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Status).IsEqualTo(ResultStatus.Warning);
    }

    [Test]
    public async Task Ok_WhenCalled_ShouldHaveNoOriginOrException()
    {
        // Arrange & Act
        Result result = Result.Ok();

        // Assert
        await Assert.That(result.Origin).IsNull();
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Messages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Fail_WhenCalled_ShouldCaptureOrigin()
    {
        // Arrange & Act
        Result result = Result.Fail();

        // Assert
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName).IsEqualTo(nameof(Fail_WhenCalled_ShouldCaptureOrigin));
        await Assert.That(result.Origin.FilePath).Contains("ResultTests.cs");
    }

    [Test]
    public async Task Fail_WhenWithExceptionCalled_ShouldCarryException()
    {
        // Arrange
        var exception = new InvalidOperationException("test error");

        // Act
        Result result = Result.Fail().WithException(exception);

        // Assert
        await Assert.That(result.Exception).IsEqualTo(exception);
    }

    [Test]
    public async Task Fail_WhenWithMessageCalled_ShouldCarryMessage()
    {
        // Arrange & Act
        Result result = Result.Fail().WithMessage("something went wrong");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("something went wrong");
    }

    [Test]
    public async Task Fail_WhenWithExceptionAndMessageCombined_ShouldCarryBoth()
    {
        // Arrange
        var exception = new InvalidOperationException("boom");

        // Act
        Result result = Result.Fail()
            .WithException(exception)
            .WithMessage("context info");

        // Assert
        await Assert.That(result.Exception).IsEqualTo(exception);
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("context info");
    }

    [Test]
    public async Task AddMessage_WhenCalledMultipleTimes_ShouldAccumulateMessages()
    {
        // Arrange
        Result result = Result.Fail();

        // Act
        result.AddMessage("first");
        result.AddMessage("second");
        result.AddMessage("third");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(3);
        await Assert.That(result.Messages[0]).IsEqualTo("first");
        await Assert.That(result.Messages[1]).IsEqualTo("second");
        await Assert.That(result.Messages[2]).IsEqualTo("third");
    }

    [Test]
    public async Task AddMessage_WhenChained_ShouldReturnSameInstance()
    {
        // Arrange
        Result result = Result.Ok();

        // Act
        var returned = result.AddMessage("msg");

        // Assert
        await Assert.That(ReferenceEquals(result, returned)).IsTrue();
    }
}
