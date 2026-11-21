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
            if (entry.Value is null && !keyContract.AllowsNull)
            {
                differences.Add(
                    $"Required metadata key '{keyContract.Name}' has a null value.");
                continue;
            }

            if (keyContract.FixedValue is not null &&
                !string.Equals(entry.Value, keyContract.FixedValue, StringComparison.Ordinal))
            {
                differences.Add(
                    $"Metadata key '{keyContract.Name}' differs. " +
                    $"Expected '{keyContract.FixedValue}'; actual '{entry.Value}'.");
            }

            if (entry.Value is not null &&
                keyContract.AllowedValues is not null &&
                keyContract.AllowedValues.Count > 0 &&
                !keyContract.AllowedValues.Contains(entry.Value, StringComparer.Ordinal))
            {
                differences.Add(
                    $"Metadata key '{keyContract.Name}' has an invalid value '{entry.Value}'.");
            }
        }

        var retiredKeys = contract.RetiredMetadataKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in observation.Metadata.Where(candidate =>
                     retiredKeys.Contains(candidate.Key)))
        {
            differences.Add($"Retired metadata key '{entry.Key}' is present.");
        }

        if (!contract.AllowsUnknownMetadataKeys)
        {
            var knownKeys = contract.MetadataKeys
                .Select(candidate => candidate.Name)
                .Concat(contract.RetiredMetadataKeys)
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
}
