using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synax.AspNetCore.ExceptionHandling;

namespace Synax.AspNetCore.UnitTests;

public class ExceptionHandlerTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "/";
        public string EnvironmentName { get; set; } = "Production";
    }

    private static SynaxExceptionHandler CreateHandler(
        string environment = "Production",
        Action<SynaxExceptionHandlerOptions>? configureOptions = null)
    {
        var options = new SynaxExceptionHandlerOptions();
        configureOptions?.Invoke(options);

        return new SynaxExceptionHandler(
            NullLogger<SynaxExceptionHandler>.Instance,
            new FakeHostEnvironment { EnvironmentName = environment },
            Options.Create(options));
    }

    private static HttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static async Task<string> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    // -------- Generic exceptions --------

    [Test]
    public async Task TryHandleAsync_WhenGenericException_ShouldReturn500()
    {
        var handler = CreateHandler();
        var context = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("boom"), CancellationToken.None);

        await Assert.That(handled).IsTrue();
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
    }

    [Test]
    public async Task TryHandleAsync_WhenGenericExceptionInProduction_ShouldHideDetails()
    {
        var handler = CreateHandler(environment: "Production");
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new InvalidOperationException("secret"), CancellationToken.None);

        var body = await ReadResponseBody(context);
        await Assert.That(body).DoesNotContain("secret");
        await Assert.That(body).Contains("unexpected error");
    }

    [Test]
    public async Task TryHandleAsync_WhenGenericExceptionInDevelopment_ShouldShowDetails()
    {
        var handler = CreateHandler(environment: "Development");
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new InvalidOperationException("detailed message"), CancellationToken.None);

        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("detailed message");
    }

    [Test]
    public async Task TryHandleAsync_WhenGenericException_ShouldReturnProblemDetails()
    {
        var handler = CreateHandler();
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new Exception("test"), CancellationToken.None);

        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("Unhandled exception");
        await Assert.That(context.Response.ContentType).Contains("application/problem+json");
    }

    // -------- OperationCanceledException --------

    [Test]
    public async Task TryHandleAsync_WhenOperationCancelled_ShouldReturn499()
    {
        var handler = CreateHandler();
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status499ClientClosedRequest);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("OperationCancelled");
    }

    // -------- Custom exception mapping --------

    [Test]
    public async Task TryHandleAsync_WhenCustomMapping_ShouldUseCustomHandler()
    {
        var handler = CreateHandler(configureOptions: opts =>
        {
            opts.MapException<ArgumentException>(ex =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "BadArgument",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity));
        });
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new ArgumentException("invalid input"), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("BadArgument");
        await Assert.That(body).Contains("invalid input");
    }

    [Test]
    public async Task TryHandleAsync_WhenMappingRegisteredForBaseType_ShouldHandleDerivedException()
    {
        var handler = CreateHandler(configureOptions: opts =>
        {
            opts.MapException<ArgumentException>(ex =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "BadArgument",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity));
        });
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new ArgumentNullException("param"), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("BadArgument");
    }

    [Test]
    public async Task TryHandleAsync_WhenBaseAndDerivedMappingsRegistered_ShouldPreferMostDerived()
    {
        var handler = CreateHandler(configureOptions: opts =>
        {
            opts.MapException<ArgumentException>(_ =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "BaseMapping",
                    statusCode: StatusCodes.Status422UnprocessableEntity));
            opts.MapException<ArgumentNullException>(_ =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "DerivedMapping",
                    statusCode: StatusCodes.Status400BadRequest));
        });
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new ArgumentNullException("param"), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("DerivedMapping");
    }

    [Test]
    public async Task TryHandleAsync_WhenResponseHasStarted_ShouldReturnTrueWithoutWriting()
    {
        var handler = CreateHandler();
        var context = CreateHttpContext();
        var startedFeature = new StartedResponseFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(startedFeature);

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("boom"), CancellationToken.None);

        await Assert.That(handled).IsTrue();
        await Assert.That(context.Response.Body.Length).IsEqualTo(0);
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    [Test]
    public async Task TryHandleAsync_WhenMappingTakesHttpContext_ShouldReceiveTheRequestContext()
    {
        var handler = CreateHandler(configureOptions: opts =>
        {
            opts.MapException<ArgumentException>((ctx, ex) =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "BadArgument",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    instance: ctx.Request.Path));
        });
        var context = CreateHttpContext();
        context.Request.Path = "/bookings/42";

        await handler.TryHandleAsync(
            context, new ArgumentException("invalid input"), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("/bookings/42");
    }

    [Test]
    public async Task TryHandleAsync_WhenCustomMappingRegistered_ShouldTakePriorityOverDefaults()
    {
        // Register a custom mapping for OperationCanceledException to override the default 499
        var handler = CreateHandler(configureOptions: opts =>
        {
            opts.MapException<OperationCanceledException>(_ =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "CustomCancelled",
                    detail: "Custom handling",
                    statusCode: StatusCodes.Status503ServiceUnavailable));
        });
        var context = CreateHttpContext();

        await handler.TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        var body = await ReadResponseBody(context);
        await Assert.That(body).Contains("CustomCancelled");
    }
}
