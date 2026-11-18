using System.Runtime.CompilerServices;

namespace Synax.Results;

/// <summary>
/// Combinators for <see cref="Result{TData, TError}"/> that cannot be expressed as instance methods
/// because they change the nullability of the result's type parameters.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Narrows a successful result with possibly-null data to a non-nullable one, failing with
    /// the error built by <paramref name="errorFactory"/> when the data is <c>null</c>.
    /// Source location is captured automatically. A failure passes through unchanged.
    /// </summary>
    /// <example>
    /// <code>
    /// Result&lt;UserDto?, HttpError&gt; fetched = await FindUserAsync(id);
    /// Result&lt;UserDto, HttpError&gt; user = fetched.EnsureNotNull(() => Errors.UserNotFound(id));
    /// </code>
    /// </example>
    public static Result<TData, TError> EnsureNotNull<TData, TError>(
        this Result<TData?, TError> result,
        Func<TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        where TData : class
    {
        if (result.IsError || result.Data is not null) return result!;

        var failure = new Result<TData, TError>(default, errorFactory(), ResultStatus.Error,
            new ResultOrigin(memberName, filePath, lineNumber), result.Exception);
        failure.AddMessagesInternal(result.Messages);
        return failure;
    }
}
