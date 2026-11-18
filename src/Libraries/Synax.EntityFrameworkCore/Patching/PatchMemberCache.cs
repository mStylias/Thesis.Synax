using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.Patching;

namespace Synax.EntityFrameworkCore.Patching;

/// <summary>
/// One patchable member: the compiled accessors that read a <c>PatchValue&lt;T&gt;</c> patch
/// property and write the same-named entity property — as a <c>SetProperty</c> call on the
/// <c>ExecuteUpdate</c> setters builder (real database) or as a direct entity assignment
/// (the in-memory Testing fakes).
/// </summary>
internal sealed record PatchMember<TEntity, TPatch>(
    string Name,
    Func<TPatch, bool> IsSet,
    Action<UpdateSettersBuilder<TEntity>, TPatch> ApplyToBuilder,
    Action<TEntity, TPatch> ApplyToEntity);

/// <summary>
/// Per (entity, patch DTO) type-pair member metadata, compiled once on first use — the hot
/// path performs no reflection (same discipline as the soft-delete / bulk-insert caches).
/// Configuration errors (no matching entity property, incompatible types, a DTO without any
/// <c>PatchValue&lt;T&gt;</c> members) fail on first use with a descriptive message.
/// </summary>
internal static class PatchMemberCache<TEntity, TPatch>
    where TEntity : class
    where TPatch : class
{
    private static readonly Lazy<IReadOnlyList<PatchMember<TEntity, TPatch>>> Cache = new(Build);

    internal static IReadOnlyList<PatchMember<TEntity, TPatch>> Members => Cache.Value;

    private static IReadOnlyList<PatchMember<TEntity, TPatch>> Build()
    {
        var members = new List<PatchMember<TEntity, TPatch>>();

        foreach (var patchProp in typeof(TPatch).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Only PatchValue<T> properties participate; plain members (route ids, flags)
            // are simply not part of the patch surface.
            if (!patchProp.CanRead
                || !patchProp.PropertyType.IsGenericType
                || patchProp.PropertyType.GetGenericTypeDefinition() != typeof(PatchValue<>))
            {
                continue;
            }

            members.Add(BuildMember(patchProp));
        }

        if (members.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{typeof(TPatch).Name}' has no {typeof(PatchValue<>).Name[..^2]}<T> properties — nothing can be patched. " +
                $"Declare the patchable members as PatchValue<T> properties named after '{typeof(TEntity).Name}' properties.");
        }

        return members;
    }

    private static PatchMember<TEntity, TPatch> BuildMember(PropertyInfo patchProp)
    {
        var entityProp = typeof(TEntity).GetProperty(patchProp.Name, BindingFlags.Public | BindingFlags.Instance);
        if (entityProp is null || !entityProp.CanWrite)
        {
            throw new InvalidOperationException(
                $"Patch property '{typeof(TPatch).Name}.{patchProp.Name}' has no writable entity property " +
                $"'{typeof(TEntity).Name}.{patchProp.Name}' to patch. Patch members are matched to entity " +
                "properties by name.");
        }

        var patchParam = Expression.Parameter(typeof(TPatch), "patch");
        var patchValueAccess = Expression.Property(patchParam, patchProp);

        // ---- IsSet: patch => patch.Member.IsSet ----
        var isSet = Expression.Lambda<Func<TPatch, bool>>(
            Expression.Property(patchValueAccess, nameof(PatchValue<>.IsSet)), patchParam).Compile();

        // ---- Value, converted to the entity property's type when they differ ----
        Expression valueExpr = Expression.Property(patchValueAccess, nameof(PatchValue<>.Value));
        if (valueExpr.Type != entityProp.PropertyType)
        {
            try
            {
                valueExpr = Expression.Convert(valueExpr, entityProp.PropertyType);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Patch property '{typeof(TPatch).Name}.{patchProp.Name}' ({valueExpr.Type.Name}) cannot be " +
                    $"assigned to '{typeof(TEntity).Name}.{patchProp.Name}' ({entityProp.PropertyType.Name}): " +
                    "no conversion exists between the types.", ex);
            }
        }

        // ---- ApplyToBuilder: (builder, patch) => builder.SetProperty(e => e.Member, value) ----
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var propertyLambda = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(TEntity), entityProp.PropertyType),
            Expression.Property(entityParam, entityProp),
            entityParam);

        var setPropertyMethod = typeof(UpdateSettersBuilder<TEntity>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(UpdateSettersBuilder<>.SetProperty))
            .Where(m => m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            .First(m => m.GetParameters()[1].ParameterType.IsGenericParameter)
            .MakeGenericMethod(entityProp.PropertyType);

        var builderParam = Expression.Parameter(typeof(UpdateSettersBuilder<TEntity>), "builder");
        var applyToBuilder = Expression.Lambda<Action<UpdateSettersBuilder<TEntity>, TPatch>>(
            Expression.Call(builderParam, setPropertyMethod, Expression.Constant(propertyLambda), valueExpr),
            builderParam, patchParam).Compile();

        // ---- ApplyToEntity: (e, patch) => e.Member = value — the in-memory flavor ----
        var applyToEntity = Expression.Lambda<Action<TEntity, TPatch>>(
            Expression.Assign(Expression.Property(entityParam, entityProp), valueExpr),
            entityParam, patchParam).Compile();

        return new PatchMember<TEntity, TPatch>(patchProp.Name, isSet, applyToBuilder, applyToEntity);
    }
}
