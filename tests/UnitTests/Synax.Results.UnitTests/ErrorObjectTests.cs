namespace Synax.Results.UnitTests;

public class ErrorObjectTests
{
    private sealed record TestError(string Message);

    [Test]
    public async Task ErrorObject_WhenNonGenericSuccess_ShouldBeNull()
    {
        Result result = Result.Ok();

        await Assert.That(result.ErrorObject).IsNull();
    }

    [Test]
    public async Task ErrorObject_WhenNonGenericFailure_ShouldBeNull()
    {
        Result result = Result.Fail();

        await Assert.That(result.ErrorObject).IsNull();
    }

    [Test]
    public async Task ErrorObject_WhenFailedResultOfTError_ShouldBeSameInstanceAsError()
    {
        var error = new TestError("boom");
        Result<TestError> result = Result.Fail(error);

        await Assert.That(ReferenceEquals(result.ErrorObject, error)).IsTrue();
        await Assert.That(ReferenceEquals(result.ErrorObject, result.Error)).IsTrue();
    }

    [Test]
    public async Task ErrorObject_WhenFailedResultOfTDataTError_ShouldBeSameInstanceAsError()
    {
        var error = new TestError("boom");
        Result<int, TestError> result = Result.Fail(error);

        await Assert.That(ReferenceEquals(result.ErrorObject, error)).IsTrue();
        await Assert.That(ReferenceEquals(result.ErrorObject, result.Error)).IsTrue();
    }

    [Test]
    public async Task ErrorObject_WhenSuccessfulResultOfTError_ShouldBeNull()
    {
        Result<TestError> result = Result.Ok();

        await Assert.That(result.ErrorObject).IsNull();
    }

    [Test]
    public async Task ErrorObject_WhenSuccessfulResultOfTDataTError_ShouldBeNull()
    {
        Result<int, TestError> result = Result.Ok(42);

        await Assert.That(result.ErrorObject).IsNull();
    }
}
