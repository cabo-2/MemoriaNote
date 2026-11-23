using System.Collections.Generic;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Provides the common shape consumed by the SQLite schema contract validator.</summary>
internal interface IDatabaseExpectedContract
{
    int ApplicationId { get; }
    int UserVersion { get; }
    IReadOnlyList<string> AuthoritativeTables { get; }
    IReadOnlyList<string> DerivedObjects { get; }
    IReadOnlyList<string> ImplementationObjects { get; }
    bool AllowsUnknownMetadataKeys { get; }
    IReadOnlyList<MetadataKeyContract> MetadataKeys { get; }
    IReadOnlyList<string> RetiredMetadataKeys { get; }
    IReadOnlyList<SchemaObjectContract> SchemaObjects { get; }
    IReadOnlyList<TableObservation> Tables { get; }
    IReadOnlyList<MigrationEntryObservation> AppliedMigrations { get; }
}

/// <summary>Defines one known metadata key and its stored-value constraints.</summary>
internal sealed record MetadataKeyContract(
    string Name,
    bool Required,
    bool AllowsNull,
    string FixedValue,
    IReadOnlyList<string> AllowedValues);

/// <summary>Defines one expected schema object without depending on raw SQL formatting.</summary>
internal sealed record SchemaObjectContract(
    string Type,
    string Name,
    string TableName,
    string Origin);

/// <summary>Requires normalized SQL fragments on one schema object.</summary>
internal sealed record SqlRequirementContract(
    string Type,
    string Name,
    IReadOnlyList<string> RequiredFragments);
