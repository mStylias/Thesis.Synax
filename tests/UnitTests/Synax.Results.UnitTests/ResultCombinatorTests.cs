using Synax.Results.UnitTests.TestModels;

namespace Synax.Results.UnitTests;

public class ResultCombinatorTests
{
    private static Result<UserDto, AppError> OkUser(int id = 1, string name = "Alice")
        => Result.Ok(new UserDto { Id = id, Name = name });

    private static Result<UserDto, AppError> FailUser(string code = "NOT_FOUND")
        => Result.Fail(new AppError { Code = code, Message = "User not found" });

    // -------- Map --------

    [Test]
    public async Task Map_WhenSuccess_ShouldTransformData()
    {
        var result = OkUser(42).Map(u => u.Id);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo(42);
    }

    [Test]
    public async Task Map_WhenWarning_ShouldPreserveStatusAndMessages()
    {
        Result<UserDto, AppError> warned = Result.Warn(new UserDto { Id = 1 });
        warned.AddMessage("cache was stale");

        var result = warned.Map(u => u.Id);

        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.Data).IsEqualTo(1);
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("cache was stale");
    }

    [Test]
    public async Task Map_WhenError_ShouldPassThroughWithoutInvokingMapper()
    {
        var invoked = false;
        Result<UserDto, AppError> source = Result
            .Fail(new AppError { Code = "ERR" })
            .WithException(new InvalidOperationException("boom"))
            .WithMessage("context");

        var result = source.Map(u => { invoked = true; return u.Id; });

        await Assert.That(invoked).IsFalse();
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("ERR");
        await Assert.That(result.Origin).IsEqualTo(source.Origin);
        await Assert.That(result.Exception).IsEqualTo(source.Exception);
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("context");
    }

    // -------- Bind --------

    [Test]
    public async Task Bind_WhenSuccess_ShouldChainAndFlatten()
    {
        var result = OkUser(7).Bind(u => Result.Ok<string, AppError>($"user-{u.Id}"));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo("user-7");
    }

    [Test]
    public async Task Bind_WhenError_ShouldShortCircuit()
    {
        var invoked = false;

        var result = FailUser().Bind(u => { invoked = true; return Result.Ok<int, AppError>(u.Id); });

        await Assert.That(invoked).IsFalse();
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("NOT_FOUND");
    }

    [Test]
    public async Task Bind_WhenBinderFails_ShouldReturnBinderError()
    {
        var result = OkUser().Bind(_ => Result.Fail<string, AppError>(new AppError { Code = "DOWNSTREAM" }));

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("DOWNSTREAM");
        await Assert.That(result.Origin).IsNotNull();
    }

    [Test]
    public async Task Bind_WhenSourceIsWarning_BoundStatusWinsAndMessagesMerge()
    {
        Result<UserDto, AppError> warned = Result.Warn(new UserDto { Id = 1 });
        warned.AddMessage("from source");

        var result = warned.Bind(u => Result.Ok<int, AppError>(u.Id).AddMessage("from binder"));

        // Bound result wins: Warning does not survive a clean Success from the binder.
        await Assert.That(result.IsWarning).IsFalse();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Messages.Count).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsEqualTo("from source");
        await Assert.That(result.Messages[1]).IsEqualTo("from binder");
    }

    [Test]
    public async Task Bind_WhenSourceHasMessagesAndBinderFails_ShouldMergeMessagesIntoFailure()
    {
        var source = OkUser().AddMessage("fetched from cache");

        var result = source.Bind<int>(_ => Result
            .Fail(new AppError { Code = "SAVE_FAILED" })
            .WithMessage("write rejected"));

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("SAVE_FAILED");
        await Assert.That(result.Messages.Count).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsEqualTo("fetched from cache");
        await Assert.That(result.Messages[1]).IsEqualTo("write rejected");
    }

    // -------- MapError --------

    [Test]
    public async Task MapError_WhenError_ShouldTransformError()
    {
        var result = FailUser("DB_DOWN").MapError(e => $"http:{e.Code}");

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsEqualTo("http:DB_DOWN");
        await Assert.That(result.Origin).IsNotNull();
    }

    [Test]
    public async Task MapError_WhenSuccess_ShouldPassThroughWithoutInvokingMapper()
    {
        var invoked = false;

        var result = OkUser(3).MapError(e => { invoked = true; return e.Code; });

        await Assert.That(invoked).IsFalse();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsEqualTo(3);
    }

    // -------- Ensure --------

    [Test]
    public async Task Ensure_WhenPredicateHolds_ShouldReturnSameInstance()
    {
        var source = OkUser(5);

        var result = source.Ensure(u => u.Id > 0, _ => new AppError { Code = "INVALID" });

        await Assert.That(ReferenceEquals(source, result)).IsTrue();
    }

    [Test]
    public async Task Ensure_WhenPredicateFails_ShouldCreateFailureWithOriginAndCarryMessages()
    {
        var source = OkUser(5).AddMessage("loaded");

        var result = source.Ensure(u => u.Id > 10, u => new AppError { Code = "TOO_SMALL", Message = $"{u.Id}" });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("TOO_SMALL");
        await Assert.That(result.Error.Message).IsEqualTo("5");
        await Assert.That(result.Origin).IsNotNull();
        await Assert.That(result.Origin!.MemberName)
            .IsEqualTo(nameof(Ensure_WhenPredicateFails_ShouldCreateFailureWithOriginAndCarryMessages));
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("loaded");
    }

    [Test]
    public async Task Ensure_WhenAlreadyError_ShouldPassThroughWithoutEvaluatingPredicate()
    {
        var invoked = false;
        var source = FailUser("ORIGINAL");

        var result = source.Ensure(u => { invoked = true; return true; }, _ => new AppError { Code = "OTHER" });

        await Assert.That(invoked).IsFalse();
        await Assert.That(ReferenceEquals(source, result)).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("ORIGINAL");
    }

    // -------- EnsureNotNull --------

    [Test]
    public async Task EnsureNotNull_WhenDataPresent_ShouldNarrowAndReturnSameInstance()
    {
        Result<UserDto?, AppError> source = Result.Ok<UserDto?>(new UserDto { Id = 4 });

        Result<UserDto, AppError> result = source.EnsureNotNull(() => new AppError { Code = "NULL" });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Id).IsEqualTo(4);
        await Assert.That(ReferenceEquals(source, result)).IsTrue();
    }

    [Test]
    public async Task EnsureNotNull_WhenDataIsNull_ShouldFailWithOrigin()
    {
        Result<UserDto?, AppError> source = Result.Ok<UserDto?>(null);
        source.AddMessage("looked up");

        var result = source.EnsureNotNull(() => new AppError { Code = "NOT_FOUND" });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("NOT_FOUND");
        await Assert.That(result.Origin!.MemberName)
            .IsEqualTo(nameof(EnsureNotNull_WhenDataIsNull_ShouldFailWithOrigin));
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("looked up");
    }

    [Test]
    public async Task EnsureNotNull_WhenAlreadyError_ShouldPassThroughWithoutInvokingFactory()
    {
        var invoked = false;
        Result<UserDto?, AppError> source = Result.Fail(new AppError { Code = "ORIGINAL" });

        var result = source.EnsureNotNull(() => { invoked = true; return new AppError { Code = "OTHER" }; });

        await Assert.That(invoked).IsFalse();
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("ORIGINAL");
    }

    [Test]
    public async Task EnsureNotNullAsync_ShouldComposeWithTryAsync()
    {
        var result = await Result.TryAsync(
                () => Task.FromResult<UserDto?>(null),
                ex => new AppError { Code = "DB" })
            .EnsureNotNullAsync(() => new AppError { Code = "NOT_FOUND" })
            .MapAsync(u => u.Id);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("NOT_FOUND");
    }

    // -------- Tap / TapError --------

    [Test]
    public async Task Tap_WhenSuccess_ShouldRunActionAndReturnSameInstance()
    {
        var seen = 0;
        var source = OkUser(9);

        var result = source.Tap(u => seen = u.Id);

        await Assert.That(seen).IsEqualTo(9);
        await Assert.That(ReferenceEquals(source, result)).IsTrue();
    }

    [Test]
    public async Task Tap_WhenError_ShouldNotRunAction()
    {
        var invoked = false;

        FailUser().Tap(_ => invoked = true);

        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task TapError_WhenError_ShouldRunActionAndReturnSameInstance()
    {
        string? seen = null;
        var source = FailUser("OBSERVED");

        var result = source.TapError(e => seen = e.Code);

        await Assert.That(seen).IsEqualTo("OBSERVED");
        await Assert.That(ReferenceEquals(source, result)).IsTrue();
    }

    [Test]
    public async Task Tap_WhenActionThrows_ShouldPropagate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            OkUser().Tap(_ => throw new InvalidOperationException("side effect failed"));
            return Task.CompletedTask;
        });
    }

    // -------- Match --------

    [Test]
    public async Task Match_WhenSuccess_ShouldUseSuccessBranch()
    {
        var text = OkUser(1, "Bob").Match(u => u.Name, e => e.Code);

        await Assert.That(text).IsEqualTo("Bob");
    }

    [Test]
    public async Task Match_WhenError_ShouldUseErrorBranch()
    {
        var text = FailUser("MATCHED").Match(u => u.Name, e => e.Code);

        await Assert.That(text).IsEqualTo("MATCHED");
    }

    // -------- Result<TError> (void) --------

    [Test]
    public async Task VoidBind_WhenSuccess_ShouldChainIntoDataResult()
    {
        Result<AppError> validation = Result.Ok();

        var result = validation.Bind(() => Result.Ok<int, AppError>(99));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo(99);
    }

    [Test]
    public async Task VoidBind_WhenError_ShouldShortCircuitPreservingError()
    {
        Result<AppError> validation = Result.Fail(new AppError { Code = "VALIDATION" }).WithMessage("bad input");

        var result = validation.Bind(() => Result.Ok<int, AppError>(99));

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("VALIDATION");
        await Assert.That(result.Messages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task VoidMatch_ShouldResolveBothTracks()
    {
        Result<AppError> ok = Result.Ok();
        Result<AppError> fail = Result.Fail(new AppError { Code = "E" });

        await Assert.That(ok.Match(() => "yes", e => e.Code)).IsEqualTo("yes");
        await Assert.That(fail.Match(() => "yes", e => e.Code)).IsEqualTo("E");
    }

    // -------- Async chains --------

    [Test]
    public async Task AsyncChain_HappyPath_ShouldFlowThroughAllStages()
    {
        var tapped = false;

        var result = await FetchAsync(5)
            .EnsureAsync(u => u.Id > 0, _ => new AppError { Code = "INVALID_ID" })
            .BindAsync(u => EnrichAsync(u))
            .TapAsync(name => { tapped = true; return Task.CompletedTask; })
            .MapAsync(name => name.ToUpperInvariant());

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data).IsEqualTo("USER-5");
        await Assert.That(tapped).IsTrue();
    }

    [Test]
    public async Task AsyncChain_WhenFirstStepFails_ShouldShortCircuitEverything()
    {
        var bindInvoked = false;
        var tapInvoked = false;

        var result = await FetchAsync(-1)
            .EnsureAsync(u => u.Id > 0, u => new AppError { Code = "INVALID_ID" })
            .BindAsync(u => { bindInvoked = true; return EnrichAsync(u); })
            .TapAsync(_ => tapInvoked = true)
            .MapAsync(name => name.Length);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.Code).IsEqualTo("INVALID_ID");
        await Assert.That(bindInvoked).IsFalse();
        await Assert.That(tapInvoked).IsFalse();
    }

    [Test]
    public async Task AsyncChain_MatchAsync_ShouldResolveToPlainValue()
    {
        var text = await FetchAsync(2)
            .BindAsync(u => EnrichAsync(u))
            .MatchAsync(name => $"ok:{name}", e => $"err:{e.Code}");

        await Assert.That(text).IsEqualTo("ok:user-2");
    }

    private static Task<Result<UserDto, AppError>> FetchAsync(int id)
        => Task.FromResult<Result<UserDto, AppError>>(Result.Ok(new UserDto { Id = id, Name = $"user-{id}" }));

    private static Task<Result<string, AppError>> EnrichAsync(UserDto user)
        => Task.FromResult<Result<string, AppError>>(Result.Ok(user.Name));
}
