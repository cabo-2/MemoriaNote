using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Validates an observation against the checked-in Schema V2 contract.</summary>
internal static class SchemaV2ContractValidator
{
    /// <summary>Returns every detected schema contract difference.</summary>
    internal static IReadOnlyList<string> ValidateSchema(
        SchemaV2ExpectedContract contract,
        DatabaseSchemaObservation observation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observation);

        var differences = DatabaseExpectedContractValidator
            .ValidateSchema(contract, observation)
            .ToList();
        foreach (var requirement in contract.SqlRequirements)
        {
            var candidate = observation.SchemaObjects.SingleOrDefault(schemaObject =>
                string.Equals(schemaObject.Type, requirement.Type, StringComparison.Ordinal) &&
                string.Equals(schemaObject.Name, requirement.Name, StringComparison.Ordinal));
            if (candidate?.Sql is null)
            {
                differences.Add(
                    $"Required SQL object '{requirement.Type} {requirement.Name}' is missing.");
                continue;
            }

            var normalizedSql = DatabaseExpectedContractValidator.NormalizeSql(candidate.Sql);
            foreach (var fragment in requirement.RequiredFragments)
            {
                var normalizedFragment = DatabaseExpectedContractValidator.NormalizeSql(fragment);
                if (!normalizedSql.Contains(normalizedFragment, StringComparison.OrdinalIgnoreCase))
                {
                    differences.Add(
                        $"SQL object '{requirement.Name}' is missing required fragment " +
                        $"'{fragment}'.");
                }
            }
        }

        return differences;
    }
}
