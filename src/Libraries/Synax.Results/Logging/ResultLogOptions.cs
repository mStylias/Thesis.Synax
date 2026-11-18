namespace Synax.Results.Logging;

/// <summary>
/// Controls which fields of a <see cref="Result"/> are included when logging via
/// <see cref="ResultLoggerExtensions.LogResult"/>.
/// All fields are included by default.
/// </summary>
/// <example>
/// <code>
/// // Log everything (default):
/// logger.LogResult(result);
///
/// // Omit origin and exception:
/// logger.LogResult(result, new ResultLogOptions { IncludeOrigin = false, IncludeException = false });
/// </code>
/// </example>
public sealed class ResultLogOptions
{
    /// <summary>
    /// A shared instance with all fields included. Used when no options are specified.
    /// </summary>
    public static ResultLogOptions Default { get; } = new();

    /// <summary>
    /// Whether to include <see cref="Result.Status"/> in the log message.
    /// Default is <c>true</c>.
    /// </summary>
    public bool IncludeStatus { get; init; } = true;

    /// <summary>
    /// Whether to include <see cref="Result.Messages"/> in the log message.
    /// Default is <c>true</c>.
    /// </summary>
    public bool IncludeMessages { get; init; } = true;

    /// <summary>
    /// Whether to include <see cref="Result.Exception"/> as the logged exception.
    /// Default is <c>true</c>.
    /// </summary>
    public bool IncludeException { get; init; } = true;

    /// <summary>
    /// Whether to include <see cref="Result.Origin"/> in the log message.
    /// Default is <c>true</c>.
    /// </summary>
    public bool IncludeOrigin { get; init; } = true;
}
