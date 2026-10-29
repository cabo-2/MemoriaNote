using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Reads a SQLite database without using EF Core or changing the database.</summary>
internal static class DatabaseSchemaInspector
{
    private static readonly string[] Fts5ShadowSuffixes =
    [
        "_config",
        "_content",
        "_data",
        "_docsize",
        "_idx"
    ];

    /// <summary>Opens an existing SQLite database read-only and records its observable schema.</summary>
    internal static async Task<DatabaseSchemaObservation> InspectAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The SQLite database does not exist.", fullPath);

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                fullPath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);

        var applicationId = await ReadPragmaInt32Async(
                connection,
                "application_id",
                cancellationToken)
            .ConfigureAwait(false);
        var userVersion = await ReadPragmaInt32Async(
                connection,
                "user_version",
                cancellationToken)
            .ConfigureAwait(false);
        var rawObjects = await ReadSchemaObjectsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var objects = ClassifyObjects(rawObjects);

        var tables = new List<TableObservation>();
        foreach (var schemaObject in objects.Where(candidate =>
                     candidate.Type == "table" && candidate.Origin == "explicit"))
        {
            tables.Add(await ReadTableAsync(
                    connection,
                    schemaObject.Name,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        var metadataTable = tables.SingleOrDefault(candidate => candidate.Name == "Metadata");
        var metadata = HasColumns(metadataTable, "Key", "Value")
            ? await ReadMetadataAsync(connection, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MetadataEntryObservation>();
        var migrationsTable = tables.SingleOrDefault(candidate =>
            candidate.Name == "__EFMigrationsHistory");
        var migrations = HasColumns(migrationsTable, "MigrationId", "ProductVersion")
            ? await ReadMigrationsAsync(connection, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MigrationEntryObservation>();

        return new DatabaseSchemaObservation(
            applicationId,
            userVersion,
            objects,
            tables,
            metadata,
            migrations);
    }

    private static bool HasColumns(TableObservation table, params string[] names)
    {
        if (table is null)
            return false;

        var columns = table.Columns.Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.Ordinal);
        return names.All(columns.Contains);
    }

    private static async Task<int> ReadPragmaInt32Async(
        SqliteConnection connection,
        string pragmaName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<RawSchemaObject>> ReadSchemaObjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, tbl_name, sql
            FROM sqlite_schema
            WHERE type IN ('table', 'index', 'trigger', 'view')
            ORDER BY type, name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var objects = new List<RawSchemaObject>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            objects.Add(new RawSchemaObject(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return objects;
    }

    private static IReadOnlyList<SchemaObjectObservation> ClassifyObjects(
        IReadOnlyList<RawSchemaObject> rawObjects)
    {
        var fts5Tables = rawObjects
            .Where(candidate =>
                candidate.Type == "table" &&
                candidate.Sql?.Contains("USING fts5", StringComparison.OrdinalIgnoreCase) == true)
            .Select(candidate => candidate.Name)
            .ToArray();

        return rawObjects
            .Select(candidate => new SchemaObjectObservation(
                candidate.Type,
                candidate.Name,
                candidate.TableName,
                ClassifyOrigin(candidate, fts5Tables),
                candidate.Sql))
            .OrderBy(candidate => candidate.Type, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ClassifyOrigin(
        RawSchemaObject schemaObject,
        IReadOnlyList<string> fts5Tables)
    {
        if (schemaObject.Type == "index" && schemaObject.Sql is null)
            return "auto-index";
        if (schemaObject.Name.StartsWith("sqlite_", StringComparison.Ordinal))
            return "sqlite-internal";
        if (fts5Tables.Any(ftsTable => Fts5ShadowSuffixes.Any(suffix =>
                string.Equals(schemaObject.Name, ftsTable + suffix, StringComparison.Ordinal))))
        {
            return "fts5-shadow";
        }

        return "explicit";
    }

    private static async Task<TableObservation> ReadTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = await ReadColumnsAsync(connection, tableName, cancellationToken)
            .ConfigureAwait(false);
        var foreignKeys = await ReadForeignKeysAsync(connection, tableName, cancellationToken)
            .ConfigureAwait(false);
        var indexes = await ReadIndexesAsync(connection, tableName, cancellationToken)
            .ConfigureAwait(false);
        return new TableObservation(tableName, columns, foreignKeys, indexes);
    }

    private static async Task<IReadOnlyList<ColumnObservation>> ReadColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var columns = new List<ColumnObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new ColumnObservation(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return columns.OrderBy(candidate => candidate.Id).ToArray();
    }

    private static async Task<IReadOnlyList<ForeignKeyObservation>> ReadForeignKeysAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var foreignKeys = new List<ForeignKeyObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            foreignKeys.Add(new ForeignKeyObservation(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return foreignKeys
            .OrderBy(candidate => candidate.Id)
            .ThenBy(candidate => candidate.Sequence)
            .ToArray();
    }

    private static async Task<IReadOnlyList<IndexObservation>> ReadIndexesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var headers = new List<IndexHeader>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            headers.Add(new IndexHeader(
                reader.GetString(1),
                reader.GetInt32(2) != 0,
                reader.GetString(3),
                reader.GetInt32(4) != 0));
        }

        var indexes = new List<IndexObservation>();
        foreach (var header in headers.OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            var columns = await ReadIndexColumnsAsync(connection, header.Name, cancellationToken)
                .ConfigureAwait(false);
            indexes.Add(new IndexObservation(
                header.Name,
                header.Unique,
                header.Origin,
                header.Partial,
                columns));
        }

        return indexes;
    }

    private static async Task<IReadOnlyList<IndexColumnObservation>> ReadIndexColumnsAsync(
        SqliteConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_xinfo({QuoteIdentifier(indexName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var columns = new List<IndexColumnObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new IndexColumnObservation(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.GetInt32(5) != 0));
        }

        return columns.OrderBy(candidate => candidate.Sequence).ToArray();
    }

    private static async Task<IReadOnlyList<MetadataEntryObservation>> ReadMetadataAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<MetadataEntryObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new MetadataEntryObservation(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return entries;
    }

    private static async Task<IReadOnlyList<MigrationEntryObservation>> ReadMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MigrationId, ProductVersion
            FROM __EFMigrationsHistory
            ORDER BY MigrationId;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<MigrationEntryObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new MigrationEntryObservation(reader.GetString(0), reader.GetString(1)));
        }

        return entries;
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record RawSchemaObject(string Type, string Name, string TableName, string Sql);

    private sealed record IndexHeader(string Name, bool Unique, string Origin, bool Partial);
}
