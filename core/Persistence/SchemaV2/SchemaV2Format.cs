namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Defines the persisted identity of Schema V2.</summary>
internal static class SchemaV2Format
{
    /// <summary>Gets the stable schema identifier used by contract tests.</summary>
    internal const string SchemaId = "schema-v2";

    /// <summary>Gets SHA-256(UTF-8("mn"))'s first big-endian word with its sign bit clear.</summary>
    internal const int ApplicationId = 0x6A43DE53;

    /// <summary>Gets the schema v2 notebook format version.</summary>
    internal const int UserVersion = 2;

    /// <summary>Gets the persisted metadata format version.</summary>
    internal const string MetadataVersion = "2";
}
