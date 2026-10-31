using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Validates Schema V2 databases without repairing or changing them.</summary>
internal static class SchemaV2DatabaseValidator
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    /// <summary>Runs the layered Schema V2 validation contract against an existing database.</summary>
    internal static async Task<SchemaV2DatabaseValidation> ValidateAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        var contract = await SchemaV2ExpectedContract.LoadEmbeddedAsync(cancellationToken)
            .ConfigureAwait(false);
        var observation = await DatabaseSchemaInspector
            .InspectAsync(databasePath, cancellationToken)
            .ConfigureAwait(false);

        var identityIssues = ValidateIdentity(contract, observation);
        var schemaIssues = SchemaV2ContractValidator.ValidateSchema(contract, observation);
        var integrityIssues = await ValidateIntegrityAsync(databasePath, cancellationToken)
            .ConfigureAwait(false);

        var authoritativeValues = schemaIssues.Count == 0 && integrityIssues.Count == 0
            ? SchemaV2ValidationLayerResult.Evaluated(
                await ValidateAuthoritativeValuesAsync(
                        databasePath,
                        contract,
                        observation,
                        cancellationToken)
                    .ConfigureAwait(false))
            : SchemaV2ValidationLayerResult.NotEvaluated;

        var derivedData = schemaIssues.Count == 0 &&
                          integrityIssues.Count == 0 &&
                          authoritativeValues.IsValid
            ? await SchemaV2DerivedDataValidator.ValidateAsync(
                    databasePath,
                    SchemaV2DerivedIndexSelection.All,
                    cancellationToken)
                .ConfigureAwait(false)
            : SchemaV2DerivedDataValidation.NotEvaluated;

        return new SchemaV2DatabaseValidation(
            SchemaV2ValidationLayerResult.Evaluated(identityIssues),
            SchemaV2ValidationLayerResult.Evaluated(schemaIssues),
            SchemaV2ValidationLayerResult.Evaluated(integrityIssues),
            authoritativeValues,
            derivedData);
    }

    private static IReadOnlyList<string> ValidateIdentity(
        SchemaV2ExpectedContract contract,
        DatabaseSchemaObservation observation)
    {
        var issues = new List<string>();
        if (observation.ApplicationId != contract.ApplicationId)
        {
            issues.Add(
                $"application_id differs. Expected '{contract.ApplicationId}'; " +
                $"actual '{observation.ApplicationId}'.");
        }

        if (observation.UserVersion != contract.UserVersion)
        {
            issues.Add(
                $"user_version differs. Expected '{contract.UserVersion}'; " +
                $"actual '{observation.UserVersion}'.");
        }

        var versions = observation.Metadata
            .Where(candidate => candidate.Key == "Version")
            .ToArray();
        if (versions.Length != 1)
            issues.Add("Metadata.Version must be present exactly once.");
        else if (!string.Equals(
                     versions[0].Value,
                     contract.PersistedNotebookFormatVersion,
                     StringComparison.Ordinal))
        {
            issues.Add(
                $"Metadata.Version differs. Expected " +
                $"'{contract.PersistedNotebookFormatVersion}'; actual '{versions[0].Value}'.");
        }

        return issues;
    }

    private static async Task<IReadOnlyList<string>> ValidateIntegrityAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        var issues = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var message = reader.GetString(0);
                if (!string.Equals(message, "ok", StringComparison.Ordinal))
                    issues.Add($"SQLite integrity_check: {message}");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var table = reader.GetString(0);
                var rowId = reader.IsDBNull(1)
                    ? "without rowid"
                    : reader.GetInt64(1).ToString(CultureInfo.InvariantCulture);
                var parent = reader.GetString(2);
                var foreignKey = reader.GetInt32(3).ToString(CultureInfo.InvariantCulture);
                issues.Add(
                    $"Foreign key violation in '{table}' row '{rowId}' " +
                    $"referencing '{parent}' (constraint {foreignKey}).");
            }
        }

        return issues;
    }

    private static async Task<IReadOnlyList<string>> ValidateAuthoritativeValuesAsync(
        string databasePath,
        SchemaV2ExpectedContract contract,
        DatabaseSchemaObservation observation,
        CancellationToken cancellationToken)
    {
        var issues = DatabaseExpectedContractValidator
            .ValidateMetadata(contract, observation)
            .ToList();
        ValidateRequiredMetadataValue(observation, "Name", issues);
        ValidateRequiredMetadataValue(observation, "Title", issues);

        await using var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT InternalId, PageId, Name, CreateTime, UpdateTime, Revision, Tags
            FROM Pages
            ORDER BY InternalId;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var internalId = reader.GetInt64(0);
            ValidatePageId(internalId, reader.GetString(1), issues);
            ValidateName(internalId, reader.GetString(2), issues);
            ValidateTimestamp(internalId, "CreateTime", reader.GetString(3), issues);
            ValidateTimestamp(internalId, "UpdateTime", reader.GetString(4), issues);
            if (reader.GetInt64(5) <= 0)
                issues.Add($"Pages row '{internalId}' has a non-positive Revision.");
            ValidateTags(internalId, reader.GetString(6), issues);
        }

        await AddDuplicateIssuesAsync(
                connection,
                "PageId",
                "Duplicate PageId",
                issues,
                cancellationToken)
            .ConfigureAwait(false);
        await AddDuplicateIssuesAsync(
                connection,
                "Name",
                "Duplicate page Name",
                issues,
                cancellationToken)
            .ConfigureAwait(false);
        return issues;
    }

    private static void ValidateRequiredMetadataValue(
        DatabaseSchemaObservation observation,
        string key,
        ICollection<string> issues)
    {
        var entry = observation.Metadata.SingleOrDefault(candidate => candidate.Key == key);
        if (entry is not null && entry.Value is null)
            issues.Add($"Required metadata key '{key}' has a null value.");
    }

    private static void ValidatePageId(
        long internalId,
        string value,
        ICollection<string> issues)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed) ||
            parsed == Guid.Empty ||
            !string.Equals(parsed.ToString("D"), value, StringComparison.Ordinal))
        {
            issues.Add($"Pages row '{internalId}' has an invalid canonical PageId.");
        }
    }

    private static void ValidateName(
        long internalId,
        string value,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"Pages row '{internalId}' has invalid Name whitespace.");
            return;
        }

        var scalarCount = 0;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                issues.Add($"Pages row '{internalId}' has invalid UTF-16 in Name.");
                return;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                issues.Add($"Pages row '{internalId}' has a control character in Name.");
                return;
            }

            scalarCount++;
            remaining = remaining[consumed..];
        }

        if (scalarCount is < 1 or > 255)
            issues.Add($"Pages row '{internalId}' Name must contain 1 to 255 Unicode scalars.");
    }

    private static void ValidateTimestamp(
        long internalId,
        string column,
        string value,
        ICollection<string> issues)
    {
        if (!DateTime.TryParseExact(
                value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            issues.Add($"Pages row '{internalId}' has an invalid canonical {column}.");
        }
    }

    private static void ValidateTags(
        long internalId,
        string value,
        ICollection<string> issues)
    {
        try
        {
            SchemaV2TagCodec.Decode(value);
        }
        catch (InvalidDataException exception)
        {
            issues.Add($"Pages row '{internalId}' has invalid Tags: {exception.Message}");
        }
    }

    private static async Task AddDuplicateIssuesAsync(
        SqliteConnection connection,
        string column,
        string label,
        ICollection<string> issues,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {column}, COUNT(*)
            FROM Pages
            GROUP BY {column} COLLATE BINARY
            HAVING COUNT(*) > 1
            ORDER BY {column} COLLATE BINARY;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            issues.Add($"{label} '{reader.GetString(0)}' is present.");
    }
}
