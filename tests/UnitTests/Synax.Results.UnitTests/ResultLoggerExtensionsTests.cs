using Microsoft.Extensions.Logging;
using Synax.Results.Logging;

namespace Synax.Results.UnitTests;

public class ResultLoggerExtensionsTests
{
    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    // -------- Log level mapping --------

    [Test]
    public async Task LogResult_WhenSuccess_ShouldLogAtInformation()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Ok();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
    }

    [Test]
    public async Task LogResult_WhenWarning_ShouldLogAtWarning()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Warn();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Warning);
    }

    [Test]
    public async Task LogResult_WhenError_ShouldLogAtError()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
    }

    // -------- Default options (all fields included) --------

    [Test]
    public async Task LogResult_WhenDefaultOptions_ShouldIncludeStatusInMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Message).Contains("Error");
    }

    [Test]
    public async Task LogResult_WhenDefaultOptions_ShouldIncludeMessagesInMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("something went wrong");

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Message).Contains("something went wrong");
    }

    [Test]
    public async Task LogResult_WhenDefaultOptions_ShouldIncludeOriginForFailResult()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Message).Contains("ResultLoggerExtensionsTests.cs");
    }

    [Test]
    public async Task LogResult_WhenExceptionPresent_ShouldPassExceptionToLogger()
    {
        // Arrange
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("boom");
        Result result = Result.Fail().WithException(exception);

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Exception).IsEqualTo(exception);
    }

    // -------- Selective field exclusion --------

    [Test]
    public async Task LogResult_WhenIncludeStatusFalse_ShouldNotContainStatusInMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("info");
        var options = new ResultLogOptions { IncludeStatus = false };

        // Act
        logger.LogResult(result, options);

        // Assert
        await Assert.That(logger.Entries[0].Message).DoesNotContain("Error");
    }

    [Test]
    public async Task LogResult_WhenIncludeMessagesFalse_ShouldNotContainMessagesInMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("secret detail");
        var options = new ResultLogOptions { IncludeMessages = false };

        // Act
        logger.LogResult(result, options);

        // Assert
        await Assert.That(logger.Entries[0].Message).DoesNotContain("secret detail");
    }

    [Test]
    public async Task LogResult_WhenIncludeExceptionFalse_ShouldPassNullException()
    {
        // Arrange
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("boom");
        Result result = Result.Fail().WithException(exception);
        var options = new ResultLogOptions { IncludeException = false };

        // Act
        logger.LogResult(result, options);

        // Assert
        await Assert.That(logger.Entries[0].Exception).IsNull();
    }

    [Test]
    public async Task LogResult_WhenIncludeOriginFalse_ShouldNotContainOriginInMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail();
        var options = new ResultLogOptions { IncludeOrigin = false };

        // Act
        logger.LogResult(result, options);

        // Assert
        await Assert.That(logger.Entries[0].Message).DoesNotContain("Origin");
    }

    [Test]
    public async Task LogResult_WhenAllOptionsDisabled_ShouldStillLogMinimalMessage()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("hidden").WithException(new Exception("hidden"));
        var options = new ResultLogOptions
        {
            IncludeStatus = false,
            IncludeMessages = false,
            IncludeException = false,
            IncludeOrigin = false
        };

        // Act
        logger.LogResult(result, options);

        // Assert
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Message).Contains("Result completed");
        await Assert.That(logger.Entries[0].Exception).IsNull();
    }

    // -------- Message formatting --------

    [Test]
    public async Task LogResult_WhenMultipleMessages_ShouldJoinWithSemicolon()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("first");
        result.AddMessage("second");
        result.AddMessage("third");

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Message).Contains("first; second; third");
    }

    [Test]
    public async Task LogResult_WhenNoMessages_ShouldNotCrash()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Ok();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
    }

    // -------- Null options --------

    [Test]
    public async Task LogResult_WhenNullOptions_ShouldUseDefaults()
    {
        // Arrange
        var logger = new CapturingLogger();
        Result result = Result.Fail().WithMessage("msg");

        // Act
        logger.LogResult(result, null);

        // Assert
        await Assert.That(logger.Entries[0].Message).Contains("Error");
        await Assert.That(logger.Entries[0].Message).Contains("msg");
    }

    // -------- Smart Origin handling --------

    [Test]
    public async Task LogResult_WhenOriginIsNull_ShouldNotShowOriginPlaceholder()
    {
        // Arrange — Ok() results have no Origin
        var logger = new CapturingLogger();
        Result result = Result.Ok();

        // Act
        logger.LogResult(result);

        // Assert
        await Assert.That(logger.Entries[0].Message).DoesNotContain("Origin");
    }
}
