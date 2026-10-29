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
    IReadOnlyList<SchemaObjectContract> SchemaObjects { get; }
    IReadOnlyList<TableObservation> Tables { get; }
    IReadOnlyList<MigrationEntryObservation> AppliedMigrations { get; }
    Fts5Contract Fts5 { get; }
}

/// <summary>Defines one known metadata key and any fixed value.</summary>
internal sealed record MetadataKeyContract(string Name, bool Required, string FixedValue);

/// <summary>Defines one expected schema object without depending on raw SQL formatting.</summary>
internal sealed record SchemaObjectContract(
    string Type,
    string Name,
    string TableName,
    string Origin);

/// <summary>Defines the external-content FTS5 configuration.</summary>
internal sealed record Fts5Contract(
    string Table,
    string ContentTable,
    string ContentRowId,
    string Tokenizer,
    IReadOnlyList<string> IndexedColumns);

/// <summary>Requires normalized SQL fragments on one schema object.</summary>
internal sealed record SqlRequirementContract(
    string Type,
    string Name,
    IReadOnlyList<string> RequiredFragments);
