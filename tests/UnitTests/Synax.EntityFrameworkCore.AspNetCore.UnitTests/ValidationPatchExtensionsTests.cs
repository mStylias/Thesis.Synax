using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Patching;

namespace Synax.EntityFrameworkCore.AspNetCore.UnitTests;

public class ValidationPatchExtensionsTests
{
    private const string Code = "INVALID_NAME";
    private const string Description = "Name must not be empty.";

    // -------- Absent members --------

    [Test]
    public async Task RequireIfSet_WhenUnset_ShouldRecordNoError()
    {
        var validation = new Validation()
            .RequireIfSet(PatchValue<string>.Unset, _ => false, Code, Description, "name");

        await Assert.That(validation.HasErrors).IsFalse();
    }

    [Test]
    public async Task RequireIfSet_WhenUnset_ShouldNotInvokePredicate()
    {
        // The reason the condition is a predicate and not a bool. An unset member's Value is
        // default, so an eagerly evaluated rule would run against null and throw on precisely the
        // partial updates it exists to permit.
        var invoked = false;

        new Validation().RequireIfSet(
            PatchValue<string>.Unset,
            _ =>
            {
                invoked = true;
                return true;
            },
            Code, Description);

        await Assert.That(invoked).IsFalse();
    }

    // -------- Supplied members --------

    [Test]
    public async Task RequireIfSet_WhenSetAndPredicatePasses_ShouldRecordNoError()
    {
        var validation = new Validation()
            .RequireIfSet(PatchValue<string>.Of("Alice"),
                name => !string.IsNullOrWhiteSpace(name), Code, Description, "name");

        await Assert.That(validation.HasErrors).IsFalse();
    }

    [Test]
    public async Task RequireIfSet_WhenSetAndPredicateFails_ShouldRecordTheError()
    {
        var validation = new Validation("The update is invalid.")
            .RequireIfSet(PatchValue<string>.Of("   "),
                name => !string.IsNullOrWhiteSpace(name), Code, Description, "name");

        await Assert.That(validation.HasErrors).IsTrue();

        var errors = validation.Error.ValidationErrors!.ToList();
        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(Code);
        await Assert.That(errors[0].Description).IsEqualTo(Description);
        await Assert.That(errors[0].PropertyName).IsEqualTo("name");
    }

    [Test]
    public async Task RequireIfSet_WhenSetToNull_ShouldInvokePredicateWithNull()
    {
        // An explicit null is "clear this field", which is a supplied value like any other — a
        // rule that means to allow clearing has to say so itself.
        string? observed = "not-null";
        var invoked = false;

        var validation = new Validation()
            .RequireIfSet(PatchValue<string?>.Of(null),
                value =>
                {
                    invoked = true;
                    observed = value;
                    return false;
                },
                Code, Description);

        await Assert.That(invoked).IsTrue();
        await Assert.That(observed).IsNull();
        await Assert.That(validation.HasErrors).IsTrue();
    }

    [Test]
    public async Task RequireIfSet_WithValueTypeMember_ShouldEvaluateTheSuppliedValue()
    {
        var passing = new Validation()
            .RequireIfSet(PatchValue<int>.Of(5), capacity => capacity > 0, Code, Description);
        var failing = new Validation()
            .RequireIfSet(PatchValue<int>.Of(0), capacity => capacity > 0, Code, Description);

        await Assert.That(passing.HasErrors).IsFalse();
        await Assert.That(failing.HasErrors).IsTrue();
    }

    // -------- Composition --------

    [Test]
    public async Task RequireIfSet_ShouldAccumulateAlongsideRequire()
    {
        var validation = new Validation("The update is invalid.")
            .Require(false, "ALWAYS", "Always fails.", "always")
            .RequireIfSet(PatchValue<string>.Unset, _ => false, "SKIPPED", "Never runs.", "skipped")
            .RequireIfSet(PatchValue<string>.Of(""), name => name!.Length > 0,
                Code, Description, "name");

        var errors = validation.Error.ValidationErrors!.Select(e => e.Code).ToList();

        await Assert.That(errors).Count().IsEqualTo(2);
        await Assert.That(errors).Contains("ALWAYS");
        await Assert.That(errors).Contains(Code);
        await Assert.That(errors).DoesNotContain("SKIPPED");
    }

    [Test]
    public async Task RequireIfSet_WhenFailing_ShouldStampOriginAtTheCallSiteNotTheExtension()
    {
        var validation = new Validation()
            .RequireIfSet(PatchValue<string>.Of(""), name => name!.Length > 0, Code, Description);

        var origin = validation.Error.ResultOrigin;

        // Forwarding the caller-info arguments is what keeps this pointing at the rule the
        // developer wrote; without it every patch rule in the codebase would blame one line of
        // ValidationPatchExtensions.
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.MemberName)
            .IsEqualTo(nameof(RequireIfSet_WhenFailing_ShouldStampOriginAtTheCallSiteNotTheExtension));
        await Assert.That(origin.FilePath).EndsWith("ValidationPatchExtensionsTests.cs");
    }

    // -------- Guards --------

    [Test]
    public async Task RequireIfSet_WithNullPredicate_ShouldThrow()
    {
        await Assert.That(() => new Validation()
                .RequireIfSet(PatchValue<string>.Of("x"), null!, Code, Description))
            .Throws<ArgumentNullException>();
    }
}
