using Synax.Results.UnitTests.TestModels;

namespace Synax.Results.UnitTests;

public class MessageBuildingTests
{
    [Test]
    public async Task Messages_WhenBuiltGraduallyAcrossLayers_ShouldAccumulateInOrder()
    {
        // Arrange — simulate a result passing through 3 layers
        Result<AppError> result = Result.Fail(new AppError { Code = "NOT_FOUND" })
            .WithMessage("Repository: user not found in database");

        // Act — each layer adds context
        result.AddMessage("Service: GetUserById failed");
        result.AddMessage("Controller: GET /users/42 returned error");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(3);
        await Assert.That(result.Messages[0]).IsEqualTo("Repository: user not found in database");
        await Assert.That(result.Messages[1]).IsEqualTo("Service: GetUserById failed");
        await Assert.That(result.Messages[2]).IsEqualTo("Controller: GET /users/42 returned error");
    }

    [Test]
    public async Task AddMessage_WhenChained_ShouldAccumulateAll()
    {
        // Arrange
        Result result = Result.Fail();

        // Act
        result.AddMessage("first").AddMessage("second").AddMessage("third");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(3);
        await Assert.That(result.Messages[0]).IsEqualTo("first");
        await Assert.That(result.Messages[1]).IsEqualTo("second");
        await Assert.That(result.Messages[2]).IsEqualTo("third");
    }

    [Test]
    public async Task Messages_WhenResultIsSuccess_ShouldStillAccumulate()
    {
        // Arrange
        var result = Result.Ok<int, AppError>(42);

        // Act
        result.AddMessage("informational note");

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("informational note");
    }

    [Test]
    public async Task Messages_WhenResultIsWarning_ShouldStillAccumulate()
    {
        // Arrange
        var result = Result.Warn<int, AppError>(100);

        // Act
        result.AddMessage("rate limit approaching");

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        await Assert.That(result.Messages[0]).IsEqualTo("rate limit approaching");
    }

    [Test]
    public async Task AddMessage_WhenCombinedWithFailMessage_ShouldContainAll()
    {
        // Arrange
        Result<AppError> result = Result.Fail(new AppError { Code = "ERR" })
            .WithMessage("initial reason");

        // Act
        result.AddMessage("added later");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsEqualTo("initial reason");
        await Assert.That(result.Messages[1]).IsEqualTo("added later");
    }

    [Test]
    public async Task WithMessage_WhenChainedOnUntypedFail_ShouldAccumulateAll()
    {
        // Arrange & Act
        Result result = Result.Fail()
            .WithMessage("first")
            .WithMessage("second");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsEqualTo("first");
        await Assert.That(result.Messages[1]).IsEqualTo("second");
    }

    [Test]
    public async Task WithMessage_WhenChainedOnTypedFail_ShouldAccumulateAll()
    {
        // Arrange & Act
        Result<UserDto, AppError> result = Result.Fail(new AppError { Code = "ERR" })
            .WithMessage("first")
            .WithException(new InvalidOperationException("boom"))
            .WithMessage("second");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsEqualTo("first");
        await Assert.That(result.Messages[1]).IsEqualTo("second");
        await Assert.That(result.Exception).IsNotNull();
    }

    [Test]
    public async Task Messages_WhenTypedResultPassesThroughLayers_ShouldAccumulate()
    {
        // Arrange — simulate Result<TData, TError> passing through layers
        var error = new AppError { Code = "DB_ERR", Message = "Connection refused" };
        var result = Result.Fail<UserDto, AppError>(error)
            .AddMessage("Database unreachable");

        // Act
        result.AddMessage("UserRepository: FindById failed");
        result.AddMessage("UserService: unable to fetch user");

        // Assert
        await Assert.That(result.Messages.Count).IsEqualTo(3);
        await Assert.That(result.Messages[0]).IsEqualTo("Database unreachable");
        await Assert.That(result.Messages[1]).IsEqualTo("UserRepository: FindById failed");
        await Assert.That(result.Messages[2]).IsEqualTo("UserService: unable to fetch user");
    }
}
