using Microsoft.Extensions.Logging;
using Synax.Results;

namespace Synax.Mediator.UnitTests.TestFixtures;

// Fixtures reserved for TelemetryEnrichmentTests and ResultLoggingBehaviorTests: their type
// names appear in activity operation names and log categories, so keeping them out of other
// test classes lets assertions filter precisely.

public record CodedError(string ErrorCode, string Detail) : IHasErrorCode;

// -------- Span enrichment probes --------

public record EnrichedFailCommand : ICommand<Result<CodedError>>;

public class EnrichedFailCommandHandler : ICommandHandler<EnrichedFailCommand, Result<CodedError>>
{
    public Task<Result<CodedError>> HandleAsync(EnrichedFailCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<CodedError>>(
            Result.Fail(new CodedError("ENRICHED_FAIL", "it broke")).WithMessage("first message"));
}

public record EnrichedWarnCommand : ICommand<Result<string, CodedError>>;

public class EnrichedWarnCommandHandler : ICommandHandler<EnrichedWarnCommand, Result<string, CodedError>>
{
    public Task<Result<string, CodedError>> HandleAsync(EnrichedWarnCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<string, CodedError>>(Result.Warn("worked, but note this"));
}

public record EnrichedExceptionCommand : ICommand<Result<CodedError>>;

public class EnrichedExceptionCommandHandler : ICommandHandler<EnrichedExceptionCommand, Result<CodedError>>
{
    public Task<Result<CodedError>> HandleAsync(EnrichedExceptionCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<CodedError>>(
            Result.Fail(new CodedError("ENRICHED_EXCEPTION", "it broke"))
                .WithException(new InvalidOperationException("boom")));
}

// -------- Result logging probes --------

public record LogFailCommand : ICommand<Result<CodedError>>;

public class LogFailCommandHandler : ICommandHandler<LogFailCommand, Result<CodedError>>
{
    public Task<Result<CodedError>> HandleAsync(LogFailCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<CodedError>>(
            Result.Fail(new CodedError("LOG_FAIL", "it broke")).WithMessage("row 42 was invalid"));
}

public record LogWarnCommand : ICommand<Result<string, CodedError>>;

public class LogWarnCommandHandler : ICommandHandler<LogWarnCommand, Result<string, CodedError>>
{
    public Task<Result<string, CodedError>> HandleAsync(LogWarnCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<string, CodedError>>(Result.Warn("partial success"));
}

public record LogOkCommand : ICommand<Result<CodedError>>;

public class LogOkCommandHandler : ICommandHandler<LogOkCommand, Result<CodedError>>
{
    public Task<Result<CodedError>> HandleAsync(LogOkCommand command, CancellationToken cancellationToken)
        => Task.FromResult<Result<CodedError>>(Result.Ok());
}

// -------- Collecting logger --------

public sealed record CollectedLog(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Minimal collecting <see cref="ILoggerFactory"/> — the test project references only
/// Logging.Abstractions, so there is no built-in factory to use.
/// </summary>
public sealed class CollectingLoggerFactory : ILoggerFactory
{
    public List<CollectedLog> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Entries);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class CollectingLogger(string category, List<CollectedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
                entries.Add(new CollectedLog(category, logLevel, formatter(state, exception), exception));
        }
    }
}
