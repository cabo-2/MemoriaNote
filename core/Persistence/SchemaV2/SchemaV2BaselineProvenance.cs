namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Identifies the accepted SchemaLab baseline used by Schema V2.</summary>
internal static class SchemaV2BaselineProvenance
{
    internal const int HandoffManifestVersion = 1;

    internal const string SchemaLabFreezeCommit =
        "5da289582ccf475dfe4c1d7e6dcbf3bb6b4ec4a7";

    internal const string MemoriaNoteBaselineCommit =
        "3ddede8a6240a6abe91f3f431f84a2764ed2faa6";

    internal const string CanonicalDdlSourcePath = "schemas/schema-v2.sql";

    internal const string CanonicalDdlSha256 =
        "e641601efd21623b774b292aed3fe3b64aa1b14c6318de798d0d24e535140ef7";

    internal const string ExpectedContractSourcePath =
        "contracts/schema-v2.expected.json";

    internal const string ExpectedContractSha256 =
        "63cd171db3b06b63db6b1f3396674fc36a393c53b0d8ed88abfd9a8a778be22f";

    internal const string FormatIdentitySourcePath =
        "src/MemoriaNote.SchemaLab.Core/SchemaV2Schema.cs";

    internal const string FormatIdentitySourceSha256 =
        "8304d86e42d4f6f93ee5ee3e74a33b19a658fc9b63381d785355a3b6106725e9";
}
