using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Validates the common, machine-readable portion of a SQLite schema contract.</summary>
internal static class DatabaseExpectedContractValidator
{
    /// <summary>Returns schema differences without combining identity or stored-value failures.</summary>
    internal static IReadOnlyList<string> ValidateSchema(
        IDatabaseExpectedContract contract,
        DatabaseSchemaObservation observation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observation);

        var expectedObservation = new DatabaseSchemaObservation(
            observation.ApplicationId,
            observation.UserVersion,
            contract.SchemaObjects.Select(candidate => new SchemaObjectObservation(
                candidate.Type,
                candidate.Name,
                candidate.TableName,
                candidate.Origin,
                Sql: null)).ToArray(),
            contract.Tables,
            observation.Metadata,
            contract.AppliedMigrations);
        var differences = DatabaseSchemaSemanticComparer
            .Compare(expectedObservation, observation)
            .ToList();

        ValidateObjectClassification(contract, differences);
        ValidateFts5(contract.Fts5, observation, differences);
        return differences;
    }

    /// <summary>Returns required, fixed, duplicate, and unknown metadata differences.</summary>
    internal static IReadOnlyList<string> ValidateMetadata(
        IDatabaseExpectedContract contract,
        DatabaseSchemaObservation observation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observation);

        var differences = new List<string>();
        var groups = observation.Metadata
            .GroupBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ToDictionary(candidate => candidate.Key, StringComparer.Ordinal);
        foreach (var duplicate in groups.Where(candidate => candidate.Value.Count() > 1))
            differences.Add($"Metadata key '{duplicate.Key}' is present more than once.");

        foreach (var keyContract in contract.MetadataKeys)
        {
            if (!groups.TryGetValue(keyContract.Name, out var entries))
            {
                if (keyContract.Required)
                    differences.Add($"Required metadata key '{keyContract.Name}' is missing.");
                continue;
            }

            var entry = entries.First();
            if (keyContract.FixedValue is not null &&
                !string.Equals(entry.Value, keyContract.FixedValue, StringComparison.Ordinal))
            {
                differences.Add(
                    $"Metadata key '{keyContract.Name}' differs. " +
                    $"Expected '{keyContract.FixedValue}'; actual '{entry.Value}'.");
            }
        }

        if (!contract.AllowsUnknownMetadataKeys)
        {
            var knownKeys = contract.MetadataKeys
                .Select(candidate => candidate.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var entry in observation.Metadata.Where(candidate =>
                         !knownKeys.Contains(candidate.Key)))
            {
                differences.Add($"Unknown metadata key '{entry.Key}' is present.");
            }
        }

        return differences;
    }

    internal static string NormalizeSql(string value) =>
        string.Concat(value.Where(character =>
            !char.IsWhiteSpace(character) && character != '"'));

    private static void ValidateObjectClassification(
        IDatabaseExpectedContract contract,
        ICollection<string> differences)
    {
        var explicitTables = contract.SchemaObjects
            .Where(candidate => candidate.Type == "table" && candidate.Origin == "explicit")
            .Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var table in contract.AuthoritativeTables)
        {
            if (!explicitTables.Contains(table))
                differences.Add($"Authoritative table '{table}' is not an explicit table.");
        }

        var objectNames = contract.SchemaObjects
            .Select(candidate => candidate.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var derivedObject in contract.DerivedObjects)
        {
            if (!objectNames.Contains(derivedObject))
                differences.Add($"Derived object '{derivedObject}' is not present.");
        }

        foreach (var implementationObject in contract.ImplementationObjects)
        {
            if (!objectNames.Contains(implementationObject))
                differences.Add($"Implementation object '{implementationObject}' is not present.");
        }
    }

    private static void ValidateFts5(
        Fts5Contract contract,
        DatabaseSchemaObservation observation,
        ICollection<string> differences)
    {
        var schemaObject = observation.SchemaObjects.SingleOrDefault(candidate =>
            candidate.Type == "table" && candidate.Name == contract.Table);
        if (schemaObject?.Sql is null)
        {
            differences.Add($"FTS5 table '{contract.Table}' has no observable SQL definition.");
            return;
        }

        var normalizedSql = NormalizeSql(schemaObject.Sql);
        RequireSqlFragment(
            differences,
            normalizedSql,
            $"content='{contract.ContentTable}'",
            "FTS5 content table");
        RequireSqlFragment(
            differences,
            normalizedSql,
            $"content_rowid='{contract.ContentRowId}'",
            "FTS5 content row ID");
        RequireSqlFragment(
            differences,
            normalizedSql,
            $"tokenize='{contract.Tokenizer}'",
            "FTS5 tokenizer");

        var table = observation.Tables.SingleOrDefault(candidate => candidate.Name == contract.Table);
        var visibleColumns = table?.Columns
            .Where(candidate => candidate.Hidden == 0)
            .Select(candidate => candidate.Name)
            .ToArray() ?? Array.Empty<string>();
        if (!visibleColumns.SequenceEqual(contract.IndexedColumns, StringComparer.Ordinal))
        {
            differences.Add(
                $"FTS5 indexed columns differ. Expected " +
                $"[{string.Join(", ", contract.IndexedColumns)}]; actual " +
                $"[{string.Join(", ", visibleColumns)}].");
        }
    }

    private static void RequireSqlFragment(
        ICollection<string> differences,
        string normalizedSql,
        string fragment,
        string label)
    {
        if (!normalizedSql.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            differences.Add($"{label} does not match '{fragment}'.");
    }
}
