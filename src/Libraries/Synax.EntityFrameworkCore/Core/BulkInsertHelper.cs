using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// Provider-aware bulk insert: uses COPY binary for PostgreSQL, SqlBulkCopy for SQL Server,
/// and multi-row INSERT VALUES as a generic fallback. All provider-specific types are accessed
/// via reflection to avoid hard assembly dependencies; the reflected members are compiled into
/// delegates and cached per writer type, so the per-row / per-cell hot path pays no reflection cost.
/// </summary>
internal static class BulkInsertHelper
{
    private const int DefaultBatchSize = 1000;

    /// <summary>
    /// Conservative parameter budget for one generic-fallback INSERT statement. SQLite's default
    /// limit is 999 host parameters — the lowest among common providers — so batches are sized as
    /// <c>999 / columnCount</c> rows rather than a fixed row count that could exceed the limit on
    /// wide tables.
    /// </summary>
    private const int MaxParametersPerBatch = 999;

    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    public static async Task BulkInsertAsync<TEntity>(
        DbContext dbContext,
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken) where TEntity : class
    {
        if (entities.Count == 0)
            return;

        var entityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the model for the current context.");

        // Bulk insert bypasses the change tracker, so EF's client-side value generators
        // (e.g. Guid keys) never run; run them here so keys aren't silently inserted as defaults.
        GenerateClientValues(dbContext, entityType, entities);

        // The AuditingInterceptor never runs either; apply the same creation stamps here
        // (mirrors the ownership treatment in Repository.BulkInsertAsync).
        StampCreationAudit(dbContext, entities);

        var providerName = dbContext.Database.ProviderName;

        if (providerName == PostgresProviderName)
        {
            await BulkInsertPostgresAsync(dbContext, entityType, entities, cancellationToken);
        }
        else if (providerName == SqlServerProviderName)
        {
            await BulkInsertSqlServerAsync(dbContext, entityType, entities, cancellationToken);
        }
        else
        {
            await BulkInsertGenericAsync(dbContext, entityType, entities, cancellationToken);
        }
    }

    private static async Task BulkInsertPostgresAsync<TEntity>(
        DbContext dbContext,
        IEntityType entityType,
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken) where TEntity : class
    {
        var sqlHelper = dbContext.GetService<ISqlGenerationHelper>();
        var (tableName, columns, getters) = GetEntityMetadata<TEntity>(entityType, sqlHelper);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken);

        // Use PostgreSQL COPY binary protocol via Npgsql for maximum throughput.
        // This requires Npgsql, which is always available when using the Npgsql EF Core provider.
        var npgsqlConnection = GetNpgsqlConnection(connection);

        var columnList = string.Join(", ", columns.Select(c => sqlHelper.DelimitIdentifier(c.columnName)));
        var copyCommand = $"COPY {tableName} ({columnList}) FROM STDIN (FORMAT BINARY)";

        var writer = await BeginBinaryImportAsync(npgsqlConnection, copyCommand, cancellationToken);

        try
        {
            var ops = WriterOpsCache.GetOrAdd(writer.GetType(), BuildWriterOps);

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ops.StartRow(writer, cancellationToken);

                for (var i = 0; i < columns.Count; i++)
                {
                    var value = getters[i](entity);
                    await WriteValueAsync(ops, writer, value, columns[i].storeTypeName, cancellationToken);
                }
            }

            await ops.Complete(writer, cancellationToken);
        }
        finally
        {
            if (writer is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (writer is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static async Task BulkInsertSqlServerAsync<TEntity>(
        DbContext dbContext,
        IEntityType entityType,
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken) where TEntity : class
    {
        var sqlHelper = dbContext.GetService<ISqlGenerationHelper>();
        var (tableName, columns, getters) = GetEntityMetadata<TEntity>(entityType, sqlHelper);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken);

        // Use SqlBulkCopy for SQL Server - equivalent performance to BulkExtensions.
        var dataTable = new DataTable();
        foreach (var col in columns)
        {
            var colType = Nullable.GetUnderlyingType(col.clrType) ?? col.clrType;
            dataTable.Columns.Add(col.columnName, colType);
        }

        foreach (var entity in entities)
        {
            var row = dataTable.NewRow();
            for (var i = 0; i < columns.Count; i++)
            {
                row[i] = getters[i](entity) ?? DBNull.Value;
            }
            dataTable.Rows.Add(row);
        }

        await SqlBulkCopyAsync(connection, tableName, dataTable, cancellationToken);
    }

    private static async Task BulkInsertGenericAsync<TEntity>(
        DbContext dbContext,
        IEntityType entityType,
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken) where TEntity : class
    {
        var sqlHelper = dbContext.GetService<ISqlGenerationHelper>();
        var (tableName, columns, getters) = GetEntityMetadata<TEntity>(entityType, sqlHelper);

        // Derive the batch row count from the column count so one statement never exceeds
        // narrow provider parameter limits (e.g. SQLite's default 999).
        var rowsPerBatch = Math.Clamp(MaxParametersPerBatch / Math.Max(1, columns.Count), 1, DefaultBatchSize);

        // Fallback: multi-row INSERT VALUES batches (works on any relational provider).
        for (var batchStart = 0; batchStart < entities.Count; batchStart += rowsPerBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchEnd = Math.Min(batchStart + rowsPerBatch, entities.Count);
            var sb = new StringBuilder();
            var parameters = new List<object>();

            var columnList = string.Join(", ", columns.Select(c => sqlHelper.DelimitIdentifier(c.columnName)));
            sb.Append($"INSERT INTO {tableName} ({columnList}) VALUES ");

            var paramIndex = 0;
            for (var i = batchStart; i < batchEnd; i++)
            {
                if (i > batchStart) sb.Append(", ");
                sb.Append('(');
                for (var j = 0; j < columns.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append($"{{{paramIndex}}}");
                    parameters.Add(getters[j](entities[i]) ?? DBNull.Value);
                    paramIndex++;
                }
                sb.Append(')');
            }

            await dbContext.Database.ExecuteSqlRawAsync(sb.ToString(), parameters, cancellationToken);
        }
    }

    /// <param name="columnName">The store column name.</param>
    /// <param name="clrType">The provider-facing CLR type (after value conversion, if any).</param>
    /// <param name="storeTypeName">The store type name without facets (e.g. "timestamp with time zone").</param>
    private record struct ColumnInfo(string columnName, Type clrType, string? storeTypeName);

    private static (string tableName, List<ColumnInfo> columns, List<Func<TEntity, object?>> getters)
        GetEntityMetadata<TEntity>(IEntityType entityType, ISqlGenerationHelper sqlHelper) where TEntity : class
    {
        var schema = entityType.GetSchema();
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not mapped to a table.");

        // Provider-aware quoting: [table] on SQL Server, "table" on PostgreSQL/SQLite, `table` on MySQL.
        var tableName = sqlHelper.DelimitIdentifier(table, schema);

        var properties = entityType.GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Where(p => !IsStoreGenerated(p))
            .ToList();

        var columns = new List<ColumnInfo>();
        var getters = new List<Func<TEntity, object?>>();

        foreach (var prop in properties)
        {
            var storeObjectId = StoreObjectIdentifier.Table(table, schema);
            var columnName = prop.GetColumnName(storeObjectId) ?? prop.Name;

            // Go through the relational type mapping so store types (e.g. Npgsql's default
            // "timestamp with time zone" for DateTime) and value converters (enum-to-string,
            // strongly-typed ids, ...) produce the values the column actually expects.
            var typeMapping = prop.GetRelationalTypeMapping();
            var converter = typeMapping.Converter;
            var providerClrType = converter?.ProviderClrType ?? prop.ClrType;
            var storeTypeName = StripFacets(typeMapping.StoreType);

            columns.Add(new ColumnInfo(columnName, providerClrType, storeTypeName));
            var getter = prop.GetGetter();
            getters.Add(converter is null
                ? entity => getter.GetClrValue(entity)
                : entity => converter.ConvertToProvider(getter.GetClrValue(entity)));
        }

        return (tableName, columns, getters);
    }

    /// <summary>
    /// Strips length/precision facets from a store type name, e.g. "numeric(18,2)" → "numeric",
    /// "character varying(64)" → "character varying".
    /// </summary>
    private static string StripFacets(string storeType)
    {
        var facetStart = storeType.IndexOf('(');
        return facetStart < 0 ? storeType : storeType[..facetStart].TrimEnd();
    }

    private static void StampCreationAudit<TEntity>(DbContext dbContext, IReadOnlyList<TEntity> entities)
        where TEntity : class
    {
        var isAudited = typeof(Abstractions.Auditing.IHasCreationTime).IsAssignableFrom(typeof(TEntity))
            || typeof(TEntity).GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Abstractions.Auditing.ICreatedBy<,>));

        if (!isAudited)
        {
            return;
        }

        var timeProvider = ApplicationServices.GetService<TimeProvider>(dbContext) ?? TimeProvider.System;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var userId = ApplicationServices.GetService<Abstractions.Auditing.IAuditUserProvider>(dbContext)
            ?.GetCurrentUserId();

        foreach (var entity in entities)
        {
            Auditing.AuditStamper.StampCreation(entity, utcNow, userId);
        }
    }

    /// <summary>
    /// Runs EF's client-side value generators (e.g. <c>GuidValueGenerator</c> for Guid keys) for
    /// properties marked <see cref="ValueGenerated.OnAdd"/> that are <b>not</b> store-generated,
    /// on entities whose value still equals the property's sentinel ("not set") value. Without
    /// this, bulk insert would silently write defaults such as <c>Guid.Empty</c>.
    /// </summary>
    private static void GenerateClientValues<TEntity>(
        DbContext dbContext,
        IEntityType entityType,
        IReadOnlyList<TEntity> entities) where TEntity : class
    {
        List<IProperty>? candidates = null;

        foreach (var property in entityType.GetProperties())
        {
            if (!property.IsShadowProperty()
                && property.ValueGenerated.HasFlag(ValueGenerated.OnAdd)
                && !IsStoreGenerated(property))
            {
                (candidates ??= []).Add(property);
            }
        }

        if (candidates is null)
        {
            return;
        }

        var selector = dbContext.GetService<IValueGeneratorSelector>();

        foreach (var property in candidates)
        {
            if (!selector.TrySelect(property, property.DeclaringType, out var generator) || generator is null)
            {
                continue;
            }

            var getter = property.GetGetter();
            var setter = property.PropertyInfo;
            var sentinel = property.Sentinel;

            foreach (var entity in entities)
            {
                if (!Equals(getter.GetClrValue(entity), sentinel))
                {
                    continue;
                }

                if (generator.GeneratesTemporaryValues)
                {
                    throw new InvalidOperationException(
                        $"Property '{typeof(TEntity).Name}.{property.Name}' uses a temporary value generator, " +
                        $"which cannot be used with bulk insert (temporary values are placeholders the store " +
                        $"would normally replace). Set the value explicitly before calling BulkInsertAsync.");
                }

                if (setter is null || !setter.CanWrite)
                {
                    throw new InvalidOperationException(
                        $"Property '{typeof(TEntity).Name}.{property.Name}' requires client value generation " +
                        $"for bulk insert but has no writable CLR property. Set the value explicitly before " +
                        $"calling BulkInsertAsync.");
                }

                try
                {
                    setter.SetValue(entity, generator.Next(null!));
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        $"The value generator for '{typeof(TEntity).Name}.{property.Name}' requires a tracked " +
                        $"entity entry and cannot run during bulk insert (which bypasses the change tracker). " +
                        $"Set the value explicitly before calling BulkInsertAsync.", ex);
                }
            }
        }
    }

    private static bool IsStoreGenerated(IProperty property)
    {
        // A property is store-generated if it has a ValueGenerated.OnAdd annotation
        // AND has a database default or is an identity column.
        return property.ValueGenerated.HasFlag(ValueGenerated.OnAdd)
            && (property.GetDefaultValueSql() != null
             || property.GetComputedColumnSql() != null
             || IsIdentityColumn(property));
    }

    private static bool IsIdentityColumn(IProperty property)
    {
        // Check common annotations used by Npgsql and SqlServer for identity/serial columns.
        var annotations = property.GetAnnotations().ToList();

        // Npgsql: "Npgsql:ValueGenerationStrategy" = IdentityByDefaultColumn / SerialColumn
        var npgsqlStrategy = annotations
            .FirstOrDefault(a => a.Name == "Npgsql:ValueGenerationStrategy");
        if (npgsqlStrategy?.Value != null)
        {
            var strategyName = npgsqlStrategy.Value.ToString();
            if (strategyName!.Contains("Identity") || strategyName.Contains("Serial"))
                return true;
        }

        // SqlServer: "SqlServer:ValueGenerationStrategy" = IdentityColumn
        var sqlServerStrategy = annotations
            .FirstOrDefault(a => a.Name == "SqlServer:ValueGenerationStrategy");
        if (sqlServerStrategy?.Value != null)
        {
            var strategyName = sqlServerStrategy.Value.ToString();
            if (strategyName!.Contains("Identity"))
                return true;
        }

        return false;
    }

    private static async Task EnsureConnectionOpenAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
    }

    /// <summary>
    /// Awaits a reflected return value that may be Task, Task&lt;T&gt;, ValueTask, or ValueTask&lt;T&gt;.
    /// Returns the result for generic variants, or null for non-generic.
    /// </summary>
    private static async Task<object?> AwaitReflectedAsync(object? returnValue)
    {
        if (returnValue == null) return null;

        var type = returnValue.GetType();

        // Task or Task<T>
        if (returnValue is Task task)
        {
            await task;
            var resultProp = type.GetProperty("Result");
            return resultProp?.GetValue(task);
        }

        // ValueTask or ValueTask<T> — call AsTask() via reflection
        var asTaskMethod = type.GetMethod("AsTask");
        if (asTaskMethod != null)
        {
            var asTask = (Task)asTaskMethod.Invoke(returnValue, null)!;
            await asTask;
            var resultProp = asTask.GetType().GetProperty("Result");
            return resultProp?.GetValue(asTask);
        }

        return returnValue;
    }

    // ----- Npgsql reflection helpers (avoids a hard dependency on Npgsql) -----

    private static object GetNpgsqlConnection(DbConnection connection)
    {
        var connType = connection.GetType();
        if (connType.FullName == "Npgsql.NpgsqlConnection")
            return connection;

        throw new InvalidOperationException(
            $"Expected an NpgsqlConnection but got {connType.FullName}. " +
            "Ensure the PostgreSQL provider is correctly configured.");
    }

    private static async Task<object> BeginBinaryImportAsync(
        object npgsqlConnection, string copyCommand, CancellationToken ct)
    {
        var connType = npgsqlConnection.GetType();
        var method = connType.GetMethod("BeginBinaryImportAsync",
            new[] { typeof(string), typeof(CancellationToken) })
            ?? throw new InvalidOperationException("Cannot find BeginBinaryImportAsync on NpgsqlConnection.");

        var result = await AwaitReflectedAsync(method.Invoke(npgsqlConnection, new object[] { copyCommand, ct }));
        return result ?? throw new InvalidOperationException("BeginBinaryImportAsync returned null.");
    }

    /// <summary>
    /// Compiled entry points into the Npgsql binary importer, built once per writer type
    /// (there is exactly one in practice) and reused across all bulk operations. The per-row and
    /// per-cell hot path therefore performs no method lookup, no <c>MethodInfo.Invoke</c>, and no
    /// reflected awaiting.
    /// </summary>
    private sealed class NpgsqlWriterOps
    {
        public required Func<object, CancellationToken, Task> StartRow { get; init; }
        public required Func<object, CancellationToken, Task> WriteNull { get; init; }
        public required Func<object, CancellationToken, Task> Complete { get; init; }
        public required MethodInfo? GenericWriteDefinition { get; init; }
        public required MethodInfo? ObjectWrite { get; init; }

        /// <summary>Compiled typed writes keyed by value type; null = no usable overload.</summary>
        public ConcurrentDictionary<Type, Func<object, object, string, CancellationToken, Task>?> TypedWrites { get; } = new();
    }

    private static readonly ConcurrentDictionary<Type, NpgsqlWriterOps> WriterOpsCache = new();

    private static NpgsqlWriterOps BuildWriterOps(Type writerType)
    {
        return new NpgsqlWriterOps
        {
            StartRow = CompileTokenOnlyCall(writerType, "StartRowAsync"),
            WriteNull = CompileTokenOnlyCall(writerType, "WriteNullAsync"),
            Complete = CompileTokenOnlyCall(writerType, "CompleteAsync"),
            GenericWriteDefinition = writerType.GetMethods()
                .FirstOrDefault(m => m is { Name: "WriteAsync", IsGenericMethodDefinition: true }
                    && m.GetParameters() is { Length: 3 } p
                    && p[1].ParameterType == typeof(string)
                    && p[2].ParameterType == typeof(CancellationToken)),
            ObjectWrite = writerType.GetMethod("WriteAsync",
                [typeof(object), typeof(string), typeof(CancellationToken)])
        };
    }

    /// <summary>Compiles <c>(object writer, CancellationToken ct) => ((TWriter)writer).Method(ct)</c> as a Task.</summary>
    private static Func<object, CancellationToken, Task> CompileTokenOnlyCall(Type writerType, string methodName)
    {
        var method = writerType.GetMethod(methodName, [typeof(CancellationToken)])
            ?? throw new InvalidOperationException($"Cannot find {methodName} on {writerType.Name}.");

        var writerParam = Expression.Parameter(typeof(object), "writer");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
        var call = Expression.Call(Expression.Convert(writerParam, writerType), method, ctParam);

        return Expression.Lambda<Func<object, CancellationToken, Task>>(
            AsTaskExpression(call), writerParam, ctParam).Compile();
    }

    /// <summary>Adapts a call returning Task / ValueTask / ValueTask&lt;T&gt; to a Task-typed expression.</summary>
    private static Expression AsTaskExpression(Expression call)
    {
        if (typeof(Task).IsAssignableFrom(call.Type))
        {
            return Expression.Convert(call, typeof(Task));
        }

        var asTask = call.Type.GetMethod("AsTask", Type.EmptyTypes)
            ?? throw new InvalidOperationException($"Cannot await return type {call.Type.Name}.");

        return Expression.Convert(Expression.Call(call, asTask), typeof(Task));
    }

    private static async Task WriteValueAsync(
        NpgsqlWriterOps ops, object writer, object? value, string? storeTypeName, CancellationToken ct)
    {
        if (value == null || value == DBNull.Value)
        {
            await ops.WriteNull(writer, ct);
            return;
        }

        if (value.GetType().IsEnum)
            value = Convert.ToInt32(value);

        if (storeTypeName != null)
        {
            var typedWrite = ops.TypedWrites.GetOrAdd(value.GetType(), valueType => BuildTypedWrite(ops, valueType));
            if (typedWrite != null)
            {
                await typedWrite(writer, value, storeTypeName, ct);
                return;
            }
        }

        if (ops.ObjectWrite != null)
        {
            await AwaitReflectedAsync(ops.ObjectWrite.Invoke(writer, [value, storeTypeName ?? "text", ct]));
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot find a suitable WriteAsync method on NpgsqlBinaryImporter for type {value.GetType().Name}.");
        }
    }

    /// <summary>
    /// Compiles <c>(writer, value, storeType, ct) => ((TWriter)writer).WriteAsync&lt;TValue&gt;((TValue)value, storeType, ct)</c>.
    /// </summary>
    private static Func<object, object, string, CancellationToken, Task>? BuildTypedWrite(
        NpgsqlWriterOps ops, Type valueType)
    {
        if (ops.GenericWriteDefinition is null)
        {
            return null;
        }

        MethodInfo closedMethod;
        try
        {
            closedMethod = ops.GenericWriteDefinition.MakeGenericMethod(valueType);
        }
        catch
        {
            return null;
        }

        var writerParam = Expression.Parameter(typeof(object), "writer");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var storeTypeParam = Expression.Parameter(typeof(string), "storeType");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            Expression.Convert(writerParam, closedMethod.DeclaringType!),
            closedMethod,
            Expression.Convert(valueParam, valueType),
            storeTypeParam,
            ctParam);

        return Expression.Lambda<Func<object, object, string, CancellationToken, Task>>(
            AsTaskExpression(call), writerParam, valueParam, storeTypeParam, ctParam).Compile();
    }

    // ----- SqlBulkCopy reflection helper (avoids a hard dependency on Microsoft.Data.SqlClient) -----

    private static async Task SqlBulkCopyAsync(
        DbConnection connection, string tableName, DataTable dataTable, CancellationToken ct)
    {
        var connType = connection.GetType();
        var assembly = connType.Assembly;

        // Find SqlBulkCopy type in the same assembly as the connection
        var bulkCopyType = assembly.GetType("Microsoft.Data.SqlClient.SqlBulkCopy")
            ?? throw new InvalidOperationException(
                "Cannot find SqlBulkCopy. Ensure Microsoft.Data.SqlClient is referenced.");

        // SqlBulkCopy(SqlConnection connection)
        var bulkCopy = Activator.CreateInstance(bulkCopyType, connection)!;

        try
        {
            // Set DestinationTableName
            var destProp = bulkCopyType.GetProperty("DestinationTableName")!;
            destProp.SetValue(bulkCopy, tableName);

            // Set BatchSize
            var batchSizeProp = bulkCopyType.GetProperty("BatchSize")!;
            batchSizeProp.SetValue(bulkCopy, DefaultBatchSize);

            // Map columns
            var mappingsProp = bulkCopyType.GetProperty("ColumnMappings")!;
            var mappings = mappingsProp.GetValue(bulkCopy)!;
            var addMethod = mappings.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) })!;

            foreach (DataColumn col in dataTable.Columns)
            {
                addMethod.Invoke(mappings, new object[] { col.ColumnName, col.ColumnName });
            }

            // WriteToServerAsync(DataTable, CancellationToken)
            var writeMethod = bulkCopyType.GetMethod("WriteToServerAsync",
                new[] { typeof(DataTable), typeof(CancellationToken) })!;

            var task = (Task)writeMethod.Invoke(bulkCopy, new object[] { dataTable, ct })!;
            await task;
        }
        finally
        {
            if (bulkCopy is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
