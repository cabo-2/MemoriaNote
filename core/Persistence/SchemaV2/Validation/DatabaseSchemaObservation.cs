using System.Collections.Generic;

namespace MemoriaNote.Persistence.SchemaV2.Validation;

/// <summary>Describes the observable schema and format identity of one SQLite database.</summary>
internal sealed record DatabaseSchemaObservation(
    int ApplicationId,
    int UserVersion,
    IReadOnlyList<SchemaObjectObservation> SchemaObjects,
    IReadOnlyList<TableObservation> Tables,
    IReadOnlyList<MetadataEntryObservation> Metadata,
    IReadOnlyList<MigrationEntryObservation> AppliedMigrations);

/// <summary>Describes one row from <c>sqlite_schema</c>.</summary>
internal sealed record SchemaObjectObservation(
    string Type,
    string Name,
    string TableName,
    string Origin,
    string Sql);

/// <summary>Describes one explicitly declared table or virtual table.</summary>
internal sealed record TableObservation(
    string Name,
    IReadOnlyList<ColumnObservation> Columns,
    IReadOnlyList<ForeignKeyObservation> ForeignKeys,
    IReadOnlyList<IndexObservation> Indexes);

/// <summary>Describes one column returned by <c>PRAGMA table_xinfo</c>.</summary>
internal sealed record ColumnObservation(
    int Id,
    string Name,
    string DeclaredType,
    bool NotNull,
    string DefaultValue,
    int PrimaryKeyOrdinal,
    int Hidden);

/// <summary>Describes one foreign-key column mapping.</summary>
internal sealed record ForeignKeyObservation(
    int Id,
    int Sequence,
    string TargetTable,
    string SourceColumn,
    string TargetColumn,
    string OnUpdate,
    string OnDelete,
    string Match);

/// <summary>Describes one SQLite index and its key or auxiliary columns.</summary>
internal sealed record IndexObservation(
    string Name,
    bool Unique,
    string Origin,
    bool Partial,
    IReadOnlyList<IndexColumnObservation> Columns);

/// <summary>Describes one row returned by <c>PRAGMA index_xinfo</c>.</summary>
internal sealed record IndexColumnObservation(
    int Sequence,
    int ColumnId,
    string Name,
    bool Descending,
    string Collation,
    bool Key);

/// <summary>Describes one raw key-value metadata row.</summary>
internal sealed record MetadataEntryObservation(string Key, string Value);

/// <summary>Describes one applied EF Core migration row.</summary>
internal sealed record MigrationEntryObservation(string MigrationId, string ProductVersion);
