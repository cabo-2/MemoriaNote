using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence;

/// <summary>Recognizes notebook format identity without changing the input database.</summary>
internal static class SqliteNotebookFormatRecognizer
{
    private const string SchemaV1Version = "1";

    private static readonly string[] SchemaV1Migrations =
    [
        "20230404004629_InitialCreate",
        "20230404004813_FtsIndexTable"
    ];

    /// <summary>Classifies an existing path from format identity alone.</summary>
    internal static async Task<NotebookFormatRecognition> RecognizeAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Missing,
                NotebookFormatRecognitionReason.MissingPath,
                NotebookFormatRecommendedAction.None);
        }

        try
        {
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
            if (applicationId == SchemaV2Format.ApplicationId &&
                userVersion > SchemaV2Format.UserVersion)
            {
                return new NotebookFormatRecognition(
                    NotebookFormatClassification.Unsupported,
                    NotebookFormatRecognitionReason.NewerSchemaV2Version,
                    NotebookFormatRecommendedAction.None,
                    userVersion.ToString(CultureInfo.InvariantCulture));
            }

            var hasMetadata = await HasTableAsync(
                    connection,
                    "Metadata",
                    cancellationToken)
                .ConfigureAwait(false);
            var hasMigrationHistory = await HasTableAsync(
                    connection,
                    "__EFMigrationsHistory",
                    cancellationToken)
                .ConfigureAwait(false);
            var metadataVersion = hasMetadata
                ? await ReadMetadataVersionAsync(connection, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var migrations = hasMigrationHistory
                ? await ReadMigrationsAsync(connection, cancellationToken)
                    .ConfigureAwait(false)
                : Array.Empty<string>();

            return Classify(
                applicationId,
                userVersion,
                metadataVersion,
                migrations);
        }
        catch (SqliteException exception) when (IsUnreadableIdentity(exception))
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Corrupt,
                NotebookFormatRecognitionReason.UnreadableIdentity,
                NotebookFormatRecommendedAction.None);
        }
    }

    private static NotebookFormatRecognition Classify(
        int applicationId,
        int userVersion,
        string metadataVersion,
        IReadOnlyList<string> migrations)
    {
        var hasSchemaV1Migrations = migrations.SequenceEqual(
            SchemaV1Migrations,
            StringComparer.Ordinal);

        if (applicationId == SchemaV2Format.ApplicationId &&
            userVersion == SchemaV2Format.UserVersion &&
            metadataVersion == SchemaV2Format.MetadataVersion)
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Current,
                NotebookFormatRecognitionReason.SchemaV2Identity,
                NotebookFormatRecommendedAction.None,
                SchemaV2Format.MetadataVersion);
        }

        if (hasSchemaV1Migrations && metadataVersion == SchemaV1Version)
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Legacy,
                NotebookFormatRecognitionReason.SchemaV1Identity,
                NotebookFormatRecommendedAction.Migrate,
                SchemaV1Version);
        }

        if (hasSchemaV1Migrations && IsNewerVersion(
                metadataVersion,
                SchemaV1Version))
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Unsupported,
                NotebookFormatRecognitionReason.NewerSchemaV1Version,
                NotebookFormatRecommendedAction.None,
                metadataVersion);
        }

        if (HasMemoriaNoteIdentitySignal(
                applicationId,
                metadataVersion,
                migrations))
        {
            return new NotebookFormatRecognition(
                NotebookFormatClassification.Corrupt,
                NotebookFormatRecognitionReason.ConflictingIdentity,
                NotebookFormatRecommendedAction.None);
        }

        return new NotebookFormatRecognition(
            NotebookFormatClassification.Corrupt,
            NotebookFormatRecognitionReason.UnrecognizedSqlite,
            NotebookFormatRecommendedAction.None);
    }

    private static bool HasMemoriaNoteIdentitySignal(
        int applicationId,
        string metadataVersion,
        IReadOnlyList<string> migrations)
    {
        return applicationId == SchemaV2Format.ApplicationId ||
            metadataVersion == SchemaV1Version ||
            metadataVersion == SchemaV2Format.MetadataVersion ||
            migrations.Any(candidate => SchemaV1Migrations.Contains(
                candidate,
                StringComparer.Ordinal));
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        return long.TryParse(
                candidate,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var candidateValue) &&
            long.TryParse(
                current,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var currentValue) &&
            candidateValue > currentValue;
    }

    private static async Task<int> ReadPragmaInt32Async(
        SqliteConnection connection,
        string pragmaName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_schema
            WHERE type = 'table' AND name = $name COLLATE BINARY
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false) != null;
    }

    private static async Task<string> ReadMetadataVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Value
            FROM Metadata
            WHERE Key = 'Version'
            LIMIT 1;
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<string>> ReadMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MigrationId
            FROM __EFMigrationsHistory
            ORDER BY MigrationId COLLATE BINARY;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var migrations = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            migrations.Add(reader.GetString(0));
        return migrations;
    }

    private static bool IsUnreadableIdentity(SqliteException exception)
    {
        return exception.SqliteErrorCode == 1 ||
            exception.SqliteErrorCode == 11 ||
            exception.SqliteErrorCode == 26;
    }
}
