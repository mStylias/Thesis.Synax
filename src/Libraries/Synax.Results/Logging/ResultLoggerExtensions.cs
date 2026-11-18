using Microsoft.Extensions.Logging;

namespace Synax.Results.Logging;

/// <summary>
/// High-performance <see cref="ILogger"/> extension methods for logging <see cref="Result"/> objects
/// using source-generated, cached message templates.
/// </summary>
/// <example>
/// <code>
/// Result result = Result.Fail().WithException(ex).WithMessage("User not found");
/// logger.LogResult(result);
/// // Output: Result Error: User not found [Origin: GetUser at /src/UserService.cs:42]
/// </code>
/// </example>
public static partial class ResultLoggerExtensions
{
    /// <summary>
    /// Logs the contents of a <see cref="Result"/> at a level determined by its
    /// <see cref="Result.Status"/>: Success maps to Information, Warning to Warning, Error to Error.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="result">The result to log.</param>
    /// <param name="options">
    /// Optional configuration controlling which fields appear in the log entry.
    /// When <c>null</c>, all fields are included.
    /// </param>
    public static void LogResult(this ILogger logger, Result result, ResultLogOptions? options = null)
    {
        var level = StatusToLogLevel(result.Status);

        if (!logger.IsEnabled(level))
            return;

        var opts = options ?? ResultLogOptions.Default;
        var exception = opts.IncludeException ? result.Exception : null;

        // Compute a 3-bit key from config + actual availability.
        // Origin is only included when configured AND actually present on the result.
        var key = (opts.IncludeStatus ? 4 : 0)
                | (opts.IncludeMessages ? 2 : 0)
                | (opts.IncludeOrigin && result.Origin is not null ? 1 : 0);

        switch (key)
        {
            case 7: // Status + Messages + Origin
                LogStatusMessagesOrigin(logger, level,
                    StatusToString(result.Status), FormatMessages(result.Messages),
                    result.Origin!.ToString(), exception);
                break;
            case 6: // Status + Messages
                LogStatusMessages(logger, level,
                    StatusToString(result.Status), FormatMessages(result.Messages), exception);
                break;
            case 5: // Status + Origin
                LogStatusOrigin(logger, level,
                    StatusToString(result.Status), result.Origin!.ToString(), exception);
                break;
            case 4: // Status only
                LogStatus(logger, level, StatusToString(result.Status), exception);
                break;
            case 3: // Messages + Origin
                LogMessagesOrigin(logger, level,
                    FormatMessages(result.Messages), result.Origin!.ToString(), exception);
                break;
            case 2: // Messages only
                LogMessages(logger, level, FormatMessages(result.Messages), exception);
                break;
            case 1: // Origin only
                LogOrigin(logger, level, result.Origin!.ToString(), exception);
                break;
            case 0: // Nothing (just the event + exception)
                LogMinimal(logger, level, exception);
                break;
        }
    }

    // -------- Private helpers --------

    private static LogLevel StatusToLogLevel(ResultStatus status) => status switch
    {
        ResultStatus.Success => LogLevel.Information,
        ResultStatus.Warning => LogLevel.Warning,
        ResultStatus.Error => LogLevel.Error,
        _ => LogLevel.Information
    };

    private static string StatusToString(ResultStatus status) => status switch
    {
        ResultStatus.Success => nameof(ResultStatus.Success),
        ResultStatus.Warning => nameof(ResultStatus.Warning),
        ResultStatus.Error => nameof(ResultStatus.Error),
        _ => status.ToString()
    };

    private static string FormatMessages(IReadOnlyList<string> messages) => messages.Count switch
    {
        0 => string.Empty,
        1 => messages[0],
        _ => string.Join("; ", messages)
    };

    // -------- Source-generated cached log methods --------

    [LoggerMessage(EventId = 1, Message = "Result {Status}: {Messages} [Origin: {Origin}]")]
    private static partial void LogStatusMessagesOrigin(
        ILogger logger, LogLevel level, string status, string messages, string origin, Exception? exception);

    [LoggerMessage(EventId = 2, Message = "Result {Status}: {Messages}")]
    private static partial void LogStatusMessages(
        ILogger logger, LogLevel level, string status, string messages, Exception? exception);

    [LoggerMessage(EventId = 3, Message = "Result {Status} [Origin: {Origin}]")]
    private static partial void LogStatusOrigin(
        ILogger logger, LogLevel level, string status, string origin, Exception? exception);

    [LoggerMessage(EventId = 4, Message = "Result {Status}")]
    private static partial void LogStatus(
        ILogger logger, LogLevel level, string status, Exception? exception);

    [LoggerMessage(EventId = 5, Message = "Result: {Messages} [Origin: {Origin}]")]
    private static partial void LogMessagesOrigin(
        ILogger logger, LogLevel level, string messages, string origin, Exception? exception);

    [LoggerMessage(EventId = 6, Message = "Result: {Messages}")]
    private static partial void LogMessages(
        ILogger logger, LogLevel level, string messages, Exception? exception);

    [LoggerMessage(EventId = 7, Message = "Result [Origin: {Origin}]")]
    private static partial void LogOrigin(
        ILogger logger, LogLevel level, string origin, Exception? exception);

    [LoggerMessage(EventId = 8, Message = "Result completed")]
    private static partial void LogMinimal(
        ILogger logger, LogLevel level, Exception? exception);
}
