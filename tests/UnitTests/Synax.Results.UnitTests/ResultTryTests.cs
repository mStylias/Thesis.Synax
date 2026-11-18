using Synax.Results.UnitTests.TestModels;

namespace Synax.Results.UnitTests;

public class ResultTryTests
{
    // -------- Untyped --------

    [Test]
    public async Task Try_WhenActionSucceeds_ShouldReturnSuccess()
    {
        var result = Result.Try(() => { });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Origin).IsNull();
    }

    [Test]
    public async Task Try_WhenActionThrows_ShouldReturnFailureWithExceptionAndOrigin()
    {
        var exception = new InvalidOperationException("boom");

        var result = Result.Try(() => throw exception);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Exception).IsEqualTo(exception);
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName)
            .IsEqualTo(nameof(Try_WhenActionThrows_ShouldReturnFailureWithExceptionAndOrigin));
    }

    // -------- Typed void --------

    [Test]
    public async Task TypedVoidTry_WhenActionSucceeds_ShouldReturnSuccess()
    {
        var invoked = false;

        var result = Result.Try(() => { }, ex => { invoked = true; return new AppError { Code = "X" }; });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task TypedVoidTry_WhenActionThrows_ShouldBuildErrorFromFactory()
    {
        Result<AppError> result = Result.Try(
            () => throw new InvalidOperationException("db down"),
            ex => new AppError { Code = "DB_ERROR", Message = ex.Message });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("DB_ERROR");
        await Assert.That(result.Error.Message).IsEqualTo("db down");
        await Assert.That(result.Exception).IsNotNull();
        await Assert.That(result.Origin).IsNotNull();
    }

    // -------- Typed data --------

    [Test]
    public async Task DataTry_WhenFuncSucceeds_ShouldReturnData()
    {
        var result = Result.Try(
            () => new UserDto { Id = 7, Name = "Alice" },
            ex => new AppError { Code = "X" });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsEqualTo(7);
    }

    [Test]
    public async Task DataTry_WhenFuncThrows_ShouldBuildErrorFromFactory()
    {
        var result = Result.Try<UserDto, AppError>(
            () => throw new FormatException("bad json"),
            ex => new AppError { Code = "PARSE_ERROR", Message = ex.Message });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("PARSE_ERROR");
        await Assert.That(result.Exception).IsNotNull();
        await Assert.That(result.Origin!.MemberName)
            .IsEqualTo(nameof(DataTry_WhenFuncThrows_ShouldBuildErrorFromFactory));
    }

    // -------- Cancellation passthrough --------

    [Test]
    public async Task Try_WhenOperationCanceled_ShouldPropagate()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
        {
            Result.Try(() => throw new OperationCanceledException());
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Try_WhenTaskCanceled_ShouldPropagateDerivedCancellation()
    {
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
        {
            Result.Try<UserDto, AppError>(
                () => throw new TaskCanceledException(),
                ex => new AppError { Code = "NEVER" });
            return Task.CompletedTask;
        });
    }

    // -------- Async --------

    [Test]
    public async Task TryAsync_WhenActionSucceeds_ShouldReturnSuccess()
    {
        var result = await Result.TryAsync(() => Task.CompletedTask);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task TryAsync_WhenTaskFaults_ShouldReturnFailure()
    {
        Result<AppError> result = await Result.TryAsync(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("late failure");
            },
            ex => new AppError { Code = "ASYNC_FAIL", Message = ex.Message });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("ASYNC_FAIL");
        await Assert.That(result.Error.Message).IsEqualTo("late failure");
        await Assert.That(result.Exception).IsNotNull();
    }

    [Test]
    public async Task DataTryAsync_WhenFuncSucceeds_ShouldReturnData()
    {
        var result = await Result.TryAsync(
            () => Task.FromResult(new UserDto { Id = 3 }),
            ex => new AppError { Code = "X" });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsEqualTo(3);
    }

    [Test]
    public async Task DataTryAsync_WhenSyncPartThrows_ShouldStillCatch()
    {
        var result = await Result.TryAsync<UserDto, AppError>(
            () => throw new InvalidOperationException("thrown before task"),
            ex => new AppError { Code = "SYNC_THROW" });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("SYNC_THROW");
    }

    [Test]
    public async Task TryAsync_WhenCanceled_ShouldPropagate()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Result.TryAsync(
                async () => await Task.Delay(1000, cts.Token),
                ex => new AppError { Code = "NEVER" }));
    }

    // -------- Composition with combinators --------

    [Test]
    public async Task Try_ShouldComposeWithBindAndMap()
    {
        var result = Result
            .Try(() => new UserDto { Id = 10, Name = "Bob" }, ex => new AppError { Code = "FETCH" })
            .Bind(u => Result.Ok<string, AppError>(u.Name))
            .Map(name => name.ToUpperInvariant());

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo("BOB");
    }
}
