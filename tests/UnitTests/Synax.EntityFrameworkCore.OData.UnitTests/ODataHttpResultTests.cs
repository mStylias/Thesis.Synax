using Microsoft.AspNetCore.Http.HttpResults;
using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.OData.Results;
using Synax.Results;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Covers the two HTTP-shaped entry points: the standalone
/// <c>ISynaxQuery.ToODataHttpResultAsync</c> (no Synax.Results anywhere) and the
/// <c>Result&lt;ISynaxQuery, HttpError&gt;</c> bridge from the .Results add-on.
/// </summary>
public class ODataHttpResultTests
{
    // --------- Standalone (no Results) ---------- //

    [Test]
    public async Task Standalone_ValidQuery_ReturnsOkPage()
    {
        var response = await TestData.ProductQuery()
            .ToODataHttpResultAsync(TestData.CreateRequest("?$filter=stock gt 5&$count=true"));

        var ok = response.Result as Ok<ODataPage>;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value!.Value).Count().IsEqualTo(3);
        await Assert.That(ok.Value.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Standalone_InvalidQuery_Returns400Problem()
    {
        var response = await TestData.ProductQuery()
            .ToODataHttpResultAsync(TestData.CreateRequest("?$filter=nope eq 1"));

        var problem = response.Result as ProblemHttpResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.ProblemDetails.Status).IsEqualTo(400);
        await Assert.That(problem.ProblemDetails.Title).IsEqualTo(SynaxODataQueryException.ErrorCode);
    }

    [Test]
    public async Task Standalone_ResolvesSettingsFromRequestServices()
    {
        var request = TestData.CreateRequest(
            "?$top=3",
            services => services.AddSynaxOData(options => options.MaxTop = 2));

        var response = await TestData.ProductQuery().ToODataHttpResultAsync(request);

        var problem = response.Result as ProblemHttpResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.ProblemDetails.Status).IsEqualTo(400);
    }

    // --------- Result<ISynaxQuery, HttpError> bridge (.Results add-on) ---------- //

    [Test]
    public async Task ResultBridge_SuccessfulResult_ReturnsOkPage()
    {
        var result = Result.Ok<ISynaxQuery<ProductDto>, HttpError>(TestData.ProductQuery());

        var response = await result.ToODataHttpResultAsync(TestData.CreateRequest("?$orderby=name&$top=1"));

        var ok = response.Result as Ok<ODataPage>;
        await Assert.That(ok).IsNotNull();
        await Assert.That(((ProductDto)ok!.Value!.Value[0]!).Name).IsEqualTo("Apples");
    }

    [Test]
    public async Task ResultBridge_FailedResult_ReturnsItsErrorAsProblem()
    {
        Result<ISynaxQuery<ProductDto>, HttpError> result =
            HttpError.Forbidden("No access to products.", errorCode: "PRODUCTS_FORBIDDEN");

        var response = await result.ToODataHttpResultAsync(TestData.CreateRequest("?$top=1"));

        var problem = response.Result as ProblemHttpResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.ProblemDetails.Status).IsEqualTo(403);
        await Assert.That(problem.ProblemDetails.Title).IsEqualTo("PRODUCTS_FORBIDDEN");
    }

    [Test]
    public async Task ResultBridge_InvalidQuery_Returns400ProblemWithODataErrorCode()
    {
        var result = Result.Ok<ISynaxQuery<ProductDto>, HttpError>(TestData.ProductQuery());

        var response = await result.ToODataHttpResultAsync(TestData.CreateRequest("?$filter=???"));

        var problem = response.Result as ProblemHttpResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.ProblemDetails.Status).IsEqualTo(400);
        await Assert.That(problem.ProblemDetails.Title).IsEqualTo(SynaxODataQueryException.ErrorCode);
    }
}
