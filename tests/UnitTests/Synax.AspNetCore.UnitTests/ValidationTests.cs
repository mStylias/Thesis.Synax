using Synax.AspNetCore.Errors;

namespace Synax.AspNetCore.UnitTests;

public class ValidationTests
{
    [Test]
    public async Task ToResult_WhenNothingFailed_ShouldSucceed()
    {
        var v = new Validation()
            .Require(true, "A", "ok");

        var result = v.ToResult();

        await Assert.That(v.HasErrors).IsFalse();
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Require_ShouldAccumulateAllFieldErrors_IntoOne400()
    {
        var v = new Validation("The request is invalid.")
            .Require(false, "A", "First failure.", "PropA")
            .Require(true, "B", "Passes.")
            .Require(false, "C", "Second failure.", "PropC");

        var result = v.ToResult();

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.HttpCode).IsEqualTo(400);
        await Assert.That(result.Error!.ErrorCode).IsEqualTo("VALIDATION_FAILED");
        await Assert.That(result.Error!.Description).IsEqualTo("The request is invalid.");
        await Assert.That(result.Error!.ValidationErrors!.Select(e => e.Code))
            .IsEquivalentTo(["A", "C"]);
    }

    [Test]
    public async Task Require_FirstFailure_ShouldStampTheAggregateOrigin()
    {
        var v = new Validation()
            .Require(false, "A", "fails");

        await Assert.That(v.Error.ResultOrigin).IsNotNull();
        await Assert.That(v.Error.ResultOrigin!.MemberName)
            .IsEqualTo(nameof(Require_FirstFailure_ShouldStampTheAggregateOrigin));
    }

    [Test]
    public async Task Add_ShouldRecordTheErrorUnconditionally()
    {
        var inner = new ValidationError("INNER", "Nested failure.", "items[2].name");
        var v = new Validation().Add(inner);

        await Assert.That(v.HasErrors).IsTrue();
        await Assert.That(v.Error.ValidationErrors!.Single()).IsEqualTo(inner);
    }

    [Test]
    public async Task ToResultWithData_ShouldCarryData_OnSuccess_AndTheErrorOtherwise()
    {
        var clean = new Validation();
        var ok = clean.ToResult("payload");
        await Assert.That(ok.IsSuccess).IsTrue();
        await Assert.That(ok.Data).IsEqualTo("payload");

        var dirty = new Validation().Require(false, "A", "fails");
        var fail = dirty.ToResult("payload");
        await Assert.That(fail.IsError).IsTrue();
        await Assert.That(fail.Error!.ErrorCode).IsEqualTo("VALIDATION_FAILED");
    }

    [Test]
    public async Task Error_WhenNothingFailed_ShouldThrow()
    {
        var v = new Validation();

        await Assert.That(() => _ = v.Error).Throws<InvalidOperationException>();
    }
}
