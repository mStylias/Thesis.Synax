using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Expressions;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// The one check <c>$search</c> cannot be left to <see cref="ODataQueryOptions.Validate"/>: with no
/// <see cref="ISearchBinder"/> in the request's OData container, the pipeline silently drops the
/// search clause and returns every row. Declaring searchable members is what puts the binder there,
/// so "no binder" and "nobody declared anything" are the same condition — and either way the answer
/// is a <c>400</c>, never a match-everything.
/// </summary>
internal static class SearchGuardrails
{
    /// <summary>Validates the request's top-level <c>$search</c>, if it has one.</summary>
    public static void Validate<T>(ODataQueryOptions<T> options, SynaxODataOptions settings)
    {
        if (string.IsNullOrEmpty(options.RawValues.Search))
            return;

        if (settings.SearchableMembersFor(typeof(T)).Count == 0)
            throw new SynaxODataQueryException(NotAvailable(typeof(T).Name));

        RequireBinder(options.Context, typeof(T));
    }

    /// <summary>
    /// The message for a type nothing declared members for. Shared with
    /// <see cref="SynaxSearchBinder"/>, which raises the same wording for a type reached through
    /// <c>$expand</c> — there the binder is the first place the nested element type is known.
    /// </summary>
    public static string NotAvailable(string elementTypeName)
        => $"$search is not available for '{elementTypeName}': no searchable members are declared. " +
           $"Call SynaxODataOptions.Searchable<{elementTypeName}>(x => x.SomeStringMember, ...).";

    /// <summary>
    /// Fails when the options were built without Synax's binder — a programming error rather than a
    /// client one, since it means <c>CreateQueryOptions</c> was bypassed or given other settings.
    /// </summary>
    public static void RequireBinder(ODataQueryContext context, Type elementType)
    {
        if (HasBinder(context))
            return;

        throw new InvalidOperationException(
            $"$search cannot be applied to '{elementType.Name}': the query options were built without " +
            "Synax's search binder, which would silently match every row. Build them with " +
            $"{nameof(SynaxQueryODataExtensions)}.{nameof(SynaxQueryODataExtensions.CreateQueryOptions)}<{elementType.Name}>(request, settings) " +
            "using the same SynaxODataOptions instance that declares the searchable members.");
    }

    public static bool HasBinder(ODataQueryContext context)
        => context.RequestContainer?.GetService(typeof(ISearchBinder)) is SynaxSearchBinder;
}
