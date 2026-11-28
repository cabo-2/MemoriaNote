using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Classifies whether authoritative Schema V2 records can be copied safely.</summary>
internal static class SchemaV2RepairabilityValidator
{
    /// <summary>Validates only schema required to enumerate exact authoritative records.</summary>
    internal static IReadOnlyList<string> ValidateAuthoritativeSchema(
        SchemaV2ExpectedContract contract,
        DatabaseSchemaObservation observation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observation);

        var issues = new List<string>();
        foreach (var tableName in contract.AuthoritativeTables)
        {
            var expected = contract.Tables.Single(candidate =>
                string.Equals(candidate.Name, tableName, StringComparison.Ordinal));
            var actual = observation.Tables.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, tableName, StringComparison.Ordinal));
            if (actual is null)
            {
                issues.Add($"Authoritative table '{tableName}' is missing.");
                continue;
            }

            if (!expected.Columns.SequenceEqual(actual.Columns))
                issues.Add($"Authoritative table '{tableName}' columns differ.");
            if (!expected.ForeignKeys.SequenceEqual(actual.ForeignKeys))
                issues.Add($"Authoritative table '{tableName}' foreign keys differ.");
        }

        return issues;
    }

    /// <summary>Checks SQLite storage for each authoritative table independently.</summary>
    internal static async Task<IReadOnlyList<string>> ValidateAuthoritativeIntegrityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<string> authoritativeTables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authoritativeTables);

        var issues = new List<string>();
        foreach (var table in authoritativeTables)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA integrity_check('{table.Replace("'", "''", StringComparison.Ordinal)}');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var message = reader.GetString(0);
                if (!string.Equals(message, "ok", StringComparison.Ordinal))
                {
                    issues.Add(
                        $"SQLite integrity_check for authoritative table '{table}' " +
                        "reported a problem.");
                }
            }
        }

        return issues;
    }
}
