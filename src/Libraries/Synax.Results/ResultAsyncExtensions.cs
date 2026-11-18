using System.Runtime.CompilerServices;

namespace Synax.Results;

/// <summary>
/// Asynchronous counterparts of the result combinators, enabling fluent chains across
/// <c>await</c> boundaries. Each combinator is available on a <see cref="Task{TResult}"/>-wrapped
/// result (with a sync or async continuation) and on a plain result with an async continuation.
/// </summary>
/// <example>
/// <code>
/// return await GetTableAsync(id, ct)
///     .EnsureAsync(t => t.Seats >= partySize, t => Errors.PartyTooLarge(t))
///     .BindAsync(t => ReserveAsync(t, ct))
///     .MapAsync(r => mapper.Map&lt;BookingResponse&gt;(r));
/// </code>
/// </example>
public static class ResultAsyncExtensions
{
    // ==================== Result<TData, TError> ====================

    // -------- Map --------

    /// <inheritdoc cref="Result{TData, TError}.Map{TNew}"/>
    public static async Task<Result<TNew, TError>> MapAsync<TData, TError, TNew>(
        this Task<Result<TData, TError>> resultTask, Func<TData, TNew> mapper)
        => (await resultTask.ConfigureAwait(false)).Map(mapper);

    /// <inheritdoc cref="Result{TData, TError}.Map{TNew}"/>
    public static async Task<Result<TNew, TError>> MapAsync<TData, TError, TNew>(
        this Result<TData, TError> result, Func<TData, Task<TNew>> mapper)
    {
        if (result.IsError) return result.PassErrorTo<TNew>();

        var value = await mapper(result.Data!).ConfigureAwait(false);
        return result.Map(_ => value);
    }

    /// <inheritdoc cref="Result{TData, TError}.Map{TNew}"/>
    public static async Task<Result<TNew, TError>> MapAsync<TData, TError, TNew>(
        this Task<Result<TData, TError>> resultTask, Func<TData, Task<TNew>> mapper)
        => await (await resultTask.ConfigureAwait(false)).MapAsync(mapper).ConfigureAwait(false);

    // -------- Bind --------

    /// <inheritdoc cref="Result{TData, TError}.Bind{TNew}"/>
    public static async Task<Result<TNew, TError>> BindAsync<TData, TError, TNew>(
        this Task<Result<TData, TError>> resultTask, Func<TData, Result<TNew, TError>> binder)
        => (await resultTask.ConfigureAwait(false)).Bind(binder);

    /// <inheritdoc cref="Result{TData, TError}.Bind{TNew}"/>
    public static async Task<Result<TNew, TError>> BindAsync<TData, TError, TNew>(
        this Result<TData, TError> result, Func<TData, Task<Result<TNew, TError>>> binder)
    {
        if (result.IsError) return result.PassErrorTo<TNew>();

        var next = await binder(result.Data!).ConfigureAwait(false);
        return result.Bind(_ => next);
    }

    /// <inheritdoc cref="Result{TData, TError}.Bind{TNew}"/>
    public static async Task<Result<TNew, TError>> BindAsync<TData, TError, TNew>(
        this Task<Result<TData, TError>> resultTask, Func<TData, Task<Result<TNew, TError>>> binder)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(binder).ConfigureAwait(false);

    // -------- MapError --------

    /// <inheritdoc cref="Result{TData, TError}.MapError{TNewError}"/>
    public static async Task<Result<TData, TNewError>> MapErrorAsync<TData, TError, TNewError>(
        this Task<Result<TData, TError>> resultTask, Func<TError, TNewError> mapper)
        => (await resultTask.ConfigureAwait(false)).MapError(mapper);

    // -------- Ensure --------

    /// <inheritdoc cref="Result{TData, TError}.Ensure"/>
    public static async Task<Result<TData, TError>> EnsureAsync<TData, TError>(
        this Task<Result<TData, TError>> resultTask,
        Func<TData, bool> predicate,
        Func<TData, TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, errorFactory, memberName, filePath, lineNumber);

    // -------- EnsureNotNull --------

    /// <inheritdoc cref="ResultExtensions.EnsureNotNull{TData, TError}"/>
    public static async Task<Result<TData, TError>> EnsureNotNullAsync<TData, TError>(
        this Task<Result<TData?, TError>> resultTask,
        Func<TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        where TData : class
        => (await resultTask.ConfigureAwait(false)).EnsureNotNull(errorFactory, memberName, filePath, lineNumber);

    // -------- Tap --------

    /// <inheritdoc cref="Result{TData, TError}.Tap"/>
    public static async Task<Result<TData, TError>> TapAsync<TData, TError>(
        this Task<Result<TData, TError>> resultTask, Action<TData> action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    /// <inheritdoc cref="Result{TData, TError}.Tap"/>
    public static async Task<Result<TData, TError>> TapAsync<TData, TError>(
        this Result<TData, TError> result, Func<TData, Task> action)
    {
        if (result.IsSuccess) await action(result.Data).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc cref="Result{TData, TError}.Tap"/>
    public static async Task<Result<TData, TError>> TapAsync<TData, TError>(
        this Task<Result<TData, TError>> resultTask, Func<TData, Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

    // -------- TapError --------

    /// <inheritdoc cref="Result{TData, TError}.TapError"/>
    public static async Task<Result<TData, TError>> TapErrorAsync<TData, TError>(
        this Task<Result<TData, TError>> resultTask, Action<TError> action)
        => (await resultTask.ConfigureAwait(false)).TapError(action);

    /// <inheritdoc cref="Result{TData, TError}.TapError"/>
    public static async Task<Result<TData, TError>> TapErrorAsync<TData, TError>(
        this Result<TData, TError> result, Func<TError, Task> action)
    {
        if (result.IsError) await action(result.Error).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc cref="Result{TData, TError}.TapError"/>
    public static async Task<Result<TData, TError>> TapErrorAsync<TData, TError>(
        this Task<Result<TData, TError>> resultTask, Func<TError, Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);

    // -------- Match --------

    /// <inheritdoc cref="Result{TData, TError}.Match{TResult}"/>
    public static async Task<TResult> MatchAsync<TData, TError, TResult>(
        this Task<Result<TData, TError>> resultTask, Func<TData, TResult> onSuccess, Func<TError, TResult> onError)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onError);

    // ==================== Result<TError> ====================

    // -------- Bind (void → data) --------

    /// <inheritdoc cref="Result{TError}.Bind{TData}(Func{Result{TData, TError}})"/>
    public static async Task<Result<TData, TError>> BindAsync<TData, TError>(
        this Task<Result<TError>> resultTask, Func<Result<TData, TError>> binder)
        => (await resultTask.ConfigureAwait(false)).Bind(binder);

    /// <inheritdoc cref="Result{TError}.Bind{TData}(Func{Result{TData, TError}})"/>
    public static async Task<Result<TData, TError>> BindAsync<TData, TError>(
        this Result<TError> result, Func<Task<Result<TData, TError>>> binder)
    {
        if (result.IsError) return result.PassErrorTo<TData>();

        var next = await binder().ConfigureAwait(false);
        return result.Bind(() => next);
    }

    /// <inheritdoc cref="Result{TError}.Bind{TData}(Func{Result{TData, TError}})"/>
    public static async Task<Result<TData, TError>> BindAsync<TData, TError>(
        this Task<Result<TError>> resultTask, Func<Task<Result<TData, TError>>> binder)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(binder).ConfigureAwait(false);

    // -------- Bind (void → void) --------

    /// <inheritdoc cref="Result{TError}.Bind(Func{Result{TError}})"/>
    public static async Task<Result<TError>> BindAsync<TError>(
        this Task<Result<TError>> resultTask, Func<Result<TError>> binder)
        => (await resultTask.ConfigureAwait(false)).Bind(binder);

    /// <inheritdoc cref="Result{TError}.Bind(Func{Result{TError}})"/>
    public static async Task<Result<TError>> BindAsync<TError>(
        this Result<TError> result, Func<Task<Result<TError>>> binder)
    {
        if (result.IsError) return result;

        var next = await binder().ConfigureAwait(false);
        return result.Bind(() => next);
    }

    /// <inheritdoc cref="Result{TError}.Bind(Func{Result{TError}})"/>
    public static async Task<Result<TError>> BindAsync<TError>(
        this Task<Result<TError>> resultTask, Func<Task<Result<TError>>> binder)
        => await (await resultTask.ConfigureAwait(false)).BindAsync(binder).ConfigureAwait(false);

    // -------- MapError --------

    /// <inheritdoc cref="Result{TError}.MapError{TNewError}"/>
    public static async Task<Result<TNewError>> MapErrorAsync<TError, TNewError>(
        this Task<Result<TError>> resultTask, Func<TError, TNewError> mapper)
        => (await resultTask.ConfigureAwait(false)).MapError(mapper);

    // -------- Ensure --------

    /// <inheritdoc cref="Result{TError}.Ensure"/>
    public static async Task<Result<TError>> EnsureAsync<TError>(
        this Task<Result<TError>> resultTask,
        Func<bool> predicate,
        Func<TError> errorFactory,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, errorFactory, memberName, filePath, lineNumber);

    // -------- Tap --------

    /// <inheritdoc cref="Result{TError}.Tap"/>
    public static async Task<Result<TError>> TapAsync<TError>(
        this Task<Result<TError>> resultTask, Action action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    /// <inheritdoc cref="Result{TError}.Tap"/>
    public static async Task<Result<TError>> TapAsync<TError>(
        this Result<TError> result, Func<Task> action)
    {
        if (result.IsSuccess) await action().ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc cref="Result{TError}.Tap"/>
    public static async Task<Result<TError>> TapAsync<TError>(
        this Task<Result<TError>> resultTask, Func<Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);

    // -------- TapError --------

    /// <inheritdoc cref="Result{TError}.TapError"/>
    public static async Task<Result<TError>> TapErrorAsync<TError>(
        this Task<Result<TError>> resultTask, Action<TError> action)
        => (await resultTask.ConfigureAwait(false)).TapError(action);

    /// <inheritdoc cref="Result{TError}.TapError"/>
    public static async Task<Result<TError>> TapErrorAsync<TError>(
        this Result<TError> result, Func<TError, Task> action)
    {
        if (result.IsError) await action(result.Error).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc cref="Result{TError}.TapError"/>
    public static async Task<Result<TError>> TapErrorAsync<TError>(
        this Task<Result<TError>> resultTask, Func<TError, Task> action)
        => await (await resultTask.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);

    // -------- Match --------

    /// <inheritdoc cref="Result{TError}.Match{TResult}"/>
    public static async Task<TResult> MatchAsync<TError, TResult>(
        this Task<Result<TError>> resultTask, Func<TResult> onSuccess, Func<TError, TResult> onError)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onError);
}
