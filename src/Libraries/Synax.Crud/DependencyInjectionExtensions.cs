using System.Reflection;
using Synax.Crud.Internal;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.Mediator;

namespace Synax.Crud;

/// <summary>
/// Registration for the generic CRUD handlers. Call inside the app's
/// <c>AddSynaxMediator(options =&gt; ...)</c> callback so the CRUD pipelines pick up the same
/// global behaviors (transactions, result logging) as handwritten handlers:
/// <code>
/// services.AddSynaxMediator(o =&gt;
/// {
///     o.AddHandlersFromAssembly(applicationAssembly);
///     o.AddGlobalBehavior(typeof(MyTransactionBehavior&lt;,&gt;),
///         appliesTo: t =&gt; typeof(ICrudWriteRequest).IsAssignableFrom(t));
///     o.AddSynaxCrud&lt;MyContext&gt;(applicationAssembly);
/// });
/// </code>
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Scans the assemblies for concrete requests deriving from the CRUD bases
    /// (<see cref="CrudGetRequest{TEntity,TKey,TResponse}"/>, <see cref="CrudListRequest{TEntity,TResponse}"/>,
    /// <see cref="CrudCreateRequest{TEntity,TResponse}"/>, <see cref="CrudPatchRequest{TEntity,TKey,TResponse}"/>,
    /// <see cref="CrudDeleteRequest{TEntity,TKey}"/>) and registers a generic handler for each.
    /// Conventions are validated here — at startup, not first request: the entity must expose a
    /// public <c>Id</c> property of the request's key type, create members must copy onto
    /// same-named entity properties, and patch requests must declare at least one
    /// <c>PatchValue&lt;T&gt;</c> member that matches an entity property.
    /// A handwritten handler for the same request type always wins over the generic one.
    /// </summary>
    public static MediatorOptions AddSynaxCrud<TContext>(this MediatorOptions options, params Assembly[] assemblies)
        where TContext : ISynaxContext
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var assembly in assemblies)
        foreach (var requestType in assembly.GetTypes())
        {
            if (requestType is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
                continue;

            if (FindCrudBase(requestType) is not var (baseDefinition, args))
                continue;

            options.AddHandler(BuildHandlerType<TContext>(requestType, baseDefinition, args));
        }

        return options;
    }

    /// <summary>
    /// Explicit-types variant of <see cref="AddSynaxCrud{TContext}(MediatorOptions, Assembly[])"/>:
    /// registers a generic handler for each given request type without scanning. Throws when a
    /// type does not derive from one of the CRUD request bases.
    /// </summary>
    public static MediatorOptions AddSynaxCrud<TContext>(this MediatorOptions options, params Type[] requestTypes)
        where TContext : ISynaxContext
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var requestType in requestTypes)
        {
            if (requestType is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                || FindCrudBase(requestType) is not var (baseDefinition, args))
            {
                throw new ArgumentException(
                    $"'{requestType.Name}' is not a concrete CRUD request — it must derive from one of " +
                    "the Crud*Request base classes.", nameof(requestTypes));
            }

            options.AddHandler(BuildHandlerType<TContext>(requestType, baseDefinition, args));
        }

        return options;
    }

    internal static (Type BaseDefinition, Type[] Args)? FindCrudBase(Type requestType)
    {
        for (var type = requestType.BaseType; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType)
                continue;

            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(CrudGetRequest<,,>)
                || definition == typeof(CrudListRequest<,>)
                || definition == typeof(CrudCreateRequest<,>)
                || definition == typeof(CrudPatchRequest<,,>)
                || definition == typeof(CrudDeleteRequest<,>))
            {
                return (definition, type.GetGenericArguments());
            }
        }

        return null;
    }

    private static Type BuildHandlerType<TContext>(Type requestType, Type baseDefinition, Type[] args)
        where TContext : ISynaxContext
    {
        if (baseDefinition == typeof(CrudGetRequest<,,>))
        {
            ValidateKey(requestType, entityType: args[0], keyType: args[1]);
            return typeof(CrudGetHandler<,,,,>).MakeGenericType(
                requestType, args[0], args[1], args[2], typeof(TContext));
        }

        if (baseDefinition == typeof(CrudListRequest<,>))
        {
            ValidateKey(requestType, entityType: args[0], keyType: null);
            return typeof(CrudListHandler<,,,>).MakeGenericType(
                requestType, args[0], args[1], typeof(TContext));
        }

        if (baseDefinition == typeof(CrudCreateRequest<,>))
        {
            ValidateKey(requestType, entityType: args[0], keyType: null);
            ValidateCreate(requestType, entityType: args[0]);
            return typeof(CrudCreateHandler<,,,>).MakeGenericType(
                requestType, args[0], args[1], typeof(TContext));
        }

        if (baseDefinition == typeof(CrudPatchRequest<,,>))
        {
            ValidateKey(requestType, entityType: args[0], keyType: args[1]);
            ValidatePatch(requestType, entityType: args[0]);
            return typeof(CrudPatchHandler<,,,,>).MakeGenericType(
                requestType, args[0], args[1], args[2], typeof(TContext));
        }

        ValidateKey(requestType, entityType: args[0], keyType: args[1]);
        return typeof(CrudDeleteHandler<,,,>).MakeGenericType(
            requestType, args[0], args[1], typeof(TContext));
    }

    private static void ValidateKey(Type requestType, Type entityType, Type? keyType)
    {
        var keyProperty = entityType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"CRUD request '{requestType.Name}': entity '{entityType.Name}' has no public 'Id' " +
                "property — the generic handlers locate the key by that convention. Use a " +
                "handwritten handler for entities with differently-named or composite keys.");

        if (keyType is not null && keyProperty.PropertyType != keyType)
        {
            throw new InvalidOperationException(
                $"CRUD request '{requestType.Name}' declares key type '{keyType.Name}', but " +
                $"'{entityType.Name}.Id' is '{keyProperty.PropertyType.Name}'.");
        }
    }

    private static void ValidateCreate(Type requestType, Type entityType)
    {
        if (entityType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"CRUD request '{requestType.Name}': entity '{entityType.Name}' has no public " +
                "parameterless constructor, which the generic create handler requires.");
        }

        // Compile the name-matched copy now so binding errors surface at startup.
        var validate = typeof(CrudCreateBinder<,>)
            .MakeGenericType(entityType, requestType)
            .GetMethod(nameof(CrudCreateBinder<object, object>.Validate), BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            validate.Invoke(null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void ValidatePatch(Type requestType, Type entityType)
    {
        // Mirrors ExecutePatchAsync's conventions so misconfigured patch DTOs fail at startup
        // rather than on the first PATCH request.
        var patchable = 0;
        foreach (var property in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || !property.PropertyType.IsGenericType
                || property.PropertyType.GetGenericTypeDefinition() != typeof(PatchValue<>))
            {
                continue;
            }

            patchable++;
            var entityProperty = entityType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);
            if (entityProperty is null || !entityProperty.CanWrite)
            {
                throw new InvalidOperationException(
                    $"CRUD request '{requestType.Name}': patch property '{property.Name}' has no writable " +
                    $"entity property '{entityType.Name}.{property.Name}'. Patch members are matched to " +
                    "entity properties by name.");
            }
        }

        if (patchable == 0)
        {
            throw new InvalidOperationException(
                $"CRUD request '{requestType.Name}' declares no PatchValue<T> properties — nothing can be " +
                "patched. Declare the patchable members as PatchValue<T> properties named after " +
                $"'{entityType.Name}' properties.");
        }
    }
}
