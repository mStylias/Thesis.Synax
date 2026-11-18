using System.Linq.Expressions;
using Synax.Mapper.Core;

namespace Synax.Mapper.Internal;

/// <summary>
/// Non-generic view of <see cref="MappingBuilder{TSource,TDest}"/> so the configuration
/// can compile builders of unknown type arguments without reflection.
/// </summary>
internal interface IMappingBuilder
{
    /// <summary>
    /// Composes all registered bindings into the mapping expressions and reports which
    /// destination members no binding produces a value for.
    /// </summary>
    MappingBuildResult Build(MappingBuildContext context);
}

/// <summary>
/// Lookups available to a builder while compiling: registered type maps (for nested and
/// collection bindings) and registered converters. Both return <c>null</c> on a miss.
/// </summary>
internal sealed record MappingBuildContext(
    Func<Type, Type, TypeMap?> ResolveMap,
    Func<Type, Type, LambdaExpression?> ResolveConverter);

/// <summary>
/// The outcome of compiling a single mapping.
/// </summary>
/// <param name="Expression">
/// The construction expression (<c>Func&lt;TSource, TDest&gt;</c>) used for both
/// in-memory mapping and IQueryable projection.
/// </param>
/// <param name="UpdateExpression">
/// The update expression (<c>Action&lt;TSource, TDest&gt;</c>) that assigns the same
/// bindings onto an existing destination instance. Only writable members are assigned;
/// in-memory only.
/// </param>
/// <param name="UnmappedMembers">
/// Destination members (public writable properties) left unbound — neither explicitly
/// mapped, auto-mapped, nor ignored.
/// </param>
internal sealed record MappingBuildResult(
    LambdaExpression Expression,
    LambdaExpression UpdateExpression,
    IReadOnlyList<string> UnmappedMembers);
