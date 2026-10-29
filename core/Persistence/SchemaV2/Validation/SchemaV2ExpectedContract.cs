using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Defines the machine-readable expectations for Schema V2.</summary>
internal sealed record SchemaV2ExpectedContract(
    int ObservationContractVersion,
    string PersistedNotebookFormatVersion,
    int ApplicationId,
    int UserVersion,
    IReadOnlyList<string> AuthoritativeTables,
    IReadOnlyList<string> DerivedObjects,
    IReadOnlyList<string> ImplementationObjects,
    bool AllowsUnknownMetadataKeys,
    IReadOnlyList<MetadataKeyContract> MetadataKeys,
    IReadOnlyList<SchemaObjectContract> SchemaObjects,
    IReadOnlyList<TableObservation> Tables,
    IReadOnlyList<MigrationEntryObservation> AppliedMigrations,
    Fts5Contract Fts5,
    IReadOnlyList<SqlRequirementContract> SqlRequirements) : IDatabaseExpectedContract
{
    /// <summary>Loads the product-owned schema v2 contract embedded in the Core assembly.</summary>
    internal static async Task<SchemaV2ExpectedContract> LoadEmbeddedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var stream = SchemaV2Resources.OpenExpectedContract();
        return await JsonSerializer.DeserializeAsync(
                stream,
                SchemaV2JsonSerializerContext.Default.SchemaV2ExpectedContract,
                cancellationToken)
            .ConfigureAwait(false) ??
            throw new InvalidDataException("The embedded Schema V2 contract was empty.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(SchemaV2ExpectedContract))]
internal sealed partial class SchemaV2JsonSerializerContext : JsonSerializerContext;
