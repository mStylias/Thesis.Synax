using Microsoft.AspNetCore.Http;

namespace Synax.AspNetCore.ExceptionHandling;

/// <summary>
/// Configuration for <see cref="SynaxExceptionHandler"/>.
/// Use <see cref="MapException{TException}"/> to register custom exception-to-response mappings.
/// </summary>
public sealed class SynaxExceptionHandlerOptions
{
    internal Dictionary<Type, Func<HttpContext, Exception, IResult>> ExceptionMappings { get; } = new();

    /// <summary>
    /// Registers a custom mapping from an exception type to an <see cref="IResult"/>.
    /// Custom mappings take priority over the built-in handlers and also match
    /// exceptions derived from <typeparamref name="TException"/>; when multiple
    /// mappings match, the most-derived registered type wins.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddSynaxExceptionHandler(opts =>
    /// {
    ///     opts.MapException&lt;InsufficientFundsException&gt;(ex =>
    ///         Results.Problem(title: "InsufficientFunds", detail: ex.Message, statusCode: 422));
    /// });
    /// </code>
    /// </example>
    public SynaxExceptionHandlerOptions MapException<TException>(
        Func<TException, IResult> handler) where TException : Exception
    {
        ExceptionMappings[typeof(TException)] = (_, ex) => handler((TException)ex);
        return this;
    }

    /// <summary>
    /// Registers a custom mapping that also receives the <see cref="HttpContext"/>,
    /// for mappings that need request data, services, or environment checks.
    /// </summary>
    /// <example>
    /// <code>
    /// opts.MapException&lt;InsufficientFundsException&gt;((ctx, ex) =>
    ///     Results.Problem(title: "InsufficientFunds", detail: ex.Message,
    ///         statusCode: 422, instance: ctx.Request.Path));
    /// </code>
    /// </example>
    public SynaxExceptionHandlerOptions MapException<TException>(
        Func<HttpContext, TException, IResult> handler) where TException : Exception
    {
        ExceptionMappings[typeof(TException)] = (ctx, ex) => handler(ctx, (TException)ex);
        return this;
    }
}
