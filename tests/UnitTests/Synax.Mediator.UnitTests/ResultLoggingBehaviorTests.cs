using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synax.Mediator.UnitTests.TestFixtures;
using Synax.Results.Logging;

namespace Synax.Mediator.UnitTests;

/// <summary>
/// Tests for <see cref="ResultLoggingBehavior{TRequest,TResponse}"/> registered via
/// <see cref="MediatorOptions.AddResultLogging"/>: failed and warning results are logged
/// automatically (category = request full name, level from status), successes stay silent.
/// </summary>
public class ResultLoggingBehaviorTests
{
    private static IMediator CreateMediator(CollectingLoggerFactory loggerFactory, ResultLogOptions? logOptions = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<HandlerSpy>();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSynaxMediator(opts =>
        {
            opts.AddHandlersFromAssemblyContaining<HandlerSpy>();
            opts.AddResultLogging(logOptions);
        });

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsFailedResult_ShouldLogAtErrorWithRequestCategory()
    {
        // Arrange
        var loggerFactory = new CollectingLoggerFactory();
        var mediator = CreateMediator(loggerFactory);

        // Act
        var result = await mediator.SendAsync(new LogFailCommand());

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(loggerFactory.Entries).Count().IsEqualTo(1);
        var entry = loggerFactory.Entries[0];
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(entry.Category).IsEqualTo(typeof(LogFailCommand).FullName!);
        await Assert.That(entry.Message).Contains("row 42 was invalid");
        // Default options include the origin captured at the Result.Fail call site.
        await Assert.That(entry.Message).Contains("Origin");
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsWarningResult_ShouldLogAtWarning()
    {
        // Arrange
        var loggerFactory = new CollectingLoggerFactory();
        var mediator = CreateMediator(loggerFactory);

        // Act
        var result = await mediator.SendAsync(new LogWarnCommand());

        // Assert
        await Assert.That(result.IsWarning).IsTrue();
        await Assert.That(loggerFactory.Entries).Count().IsEqualTo(1);
        var entry = loggerFactory.Entries[0];
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Category).IsEqualTo(typeof(LogWarnCommand).FullName!);
    }

    [Test]
    public async Task SendAsync_WhenHandlerReturnsSuccessfulResult_ShouldNotLog()
    {
        // Arrange
        var loggerFactory = new CollectingLoggerFactory();
        var mediator = CreateMediator(loggerFactory);

        // Act
        var result = await mediator.SendAsync(new LogOkCommand());

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(loggerFactory.Entries).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenResultLogOptionsExcludeOrigin_ShouldHonorOptions()
    {
        // Arrange
        var loggerFactory = new CollectingLoggerFactory();
        var mediator = CreateMediator(loggerFactory, new ResultLogOptions { IncludeOrigin = false });

        // Act
        var result = await mediator.SendAsync(new LogFailCommand());

        // Assert
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(loggerFactory.Entries).Count().IsEqualTo(1);
        var entry = loggerFactory.Entries[0];
        await Assert.That(entry.Message).Contains("row 42 was invalid");
        await Assert.That(entry.Message.Contains("Origin")).IsFalse();
    }
}
