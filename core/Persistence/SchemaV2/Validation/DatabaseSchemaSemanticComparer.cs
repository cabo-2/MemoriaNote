using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Compares observable SQLite schema semantics while ignoring raw SQL formatting.</summary>
internal static class DatabaseSchemaSemanticComparer
{
    /// <summary>Compares format identity, objects, table members, metadata, and migration history.</summary>
    internal static IReadOnlyList<string> Compare(
        DatabaseSchemaObservation expected,
        DatabaseSchemaObservation actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var differences = new List<string>();
        AddValueDifference(differences, "application_id", expected.ApplicationId, actual.ApplicationId);
        AddValueDifference(differences, "user_version", expected.UserVersion, actual.UserVersion);
        CompareSequence(
            differences,
            "schema objects",
            expected.SchemaObjects.Select(ObjectSignature),
            actual.SchemaObjects.Select(ObjectSignature));
        CompareSequence(
            differences,
            "metadata",
            expected.Metadata.Select(candidate => $"{candidate.Key}|{candidate.Value}"),
            actual.Metadata.Select(candidate => $"{candidate.Key}|{candidate.Value}"));
        CompareSequence(
            differences,
            "migrations",
            expected.AppliedMigrations.Select(candidate =>
                $"{candidate.MigrationId}|{candidate.ProductVersion}"),
            actual.AppliedMigrations.Select(candidate =>
                $"{candidate.MigrationId}|{candidate.ProductVersion}"));

        var expectedTables = expected.Tables.ToDictionary(candidate => candidate.Name);
        var actualTables = actual.Tables.ToDictionary(candidate => candidate.Name);
        CompareSequence(differences, "explicit tables", expectedTables.Keys, actualTables.Keys);
        foreach (var tableName in expectedTables.Keys.Intersect(actualTables.Keys)
                     .OrderBy(candidate => candidate, StringComparer.Ordinal))
        {
            var expectedTable = expectedTables[tableName];
            var actualTable = actualTables[tableName];
            CompareSequence(
                differences,
                $"{tableName} columns",
                expectedTable.Columns.Select(ColumnSignature),
                actualTable.Columns.Select(ColumnSignature));
            CompareSequence(
                differences,
                $"{tableName} foreign keys",
                expectedTable.ForeignKeys.Select(ForeignKeySignature),
                actualTable.ForeignKeys.Select(ForeignKeySignature));
            CompareSequence(
                differences,
                $"{tableName} indexes",
                expectedTable.Indexes.Select(IndexSignature),
                actualTable.Indexes.Select(IndexSignature));
        }

        return differences;
    }

    private static string ObjectSignature(SchemaObjectObservation value) =>
        $"{value.Type}|{value.Name}|{value.TableName}|{value.Origin}";

    private static string ColumnSignature(ColumnObservation value) =>
        $"{value.Id}|{value.Name}|{value.DeclaredType}|{value.NotNull}|" +
        $"{value.DefaultValue}|{value.PrimaryKeyOrdinal}|{value.Hidden}";

    private static string ForeignKeySignature(ForeignKeyObservation value) =>
        $"{value.Id}|{value.Sequence}|{value.TargetTable}|{value.SourceColumn}|" +
        $"{value.TargetColumn}|{value.OnUpdate}|{value.OnDelete}|{value.Match}";

    private static string IndexSignature(IndexObservation value) =>
        $"{value.Name}|{value.Unique}|{value.Origin}|{value.Partial}|" +
        string.Join(';', value.Columns.Select(IndexColumnSignature));

    private static string IndexColumnSignature(IndexColumnObservation value) =>
        $"{value.Sequence},{value.ColumnId},{value.Name},{value.Descending}," +
        $"{value.Collation},{value.Key}";

    private static void CompareSequence(
        ICollection<string> differences,
        string label,
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        var expectedValues = expected.OrderBy(candidate => candidate, StringComparer.Ordinal).ToArray();
        var actualValues = actual.OrderBy(candidate => candidate, StringComparer.Ordinal).ToArray();
        if (!expectedValues.SequenceEqual(actualValues, StringComparer.Ordinal))
        {
            differences.Add(
                $"{label} differ. Expected [{string.Join(", ", expectedValues)}]; " +
                $"actual [{string.Join(", ", actualValues)}].");
        }
    }

    private static void AddValueDifference<T>(
        ICollection<string> differences,
        string label,
        T expected,
        T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            differences.Add($"{label} differs. Expected '{expected}'; actual '{actual}'.");
    }
}
