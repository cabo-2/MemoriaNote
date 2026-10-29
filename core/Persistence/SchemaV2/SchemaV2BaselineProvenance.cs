namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Identifies the accepted SchemaLab baseline used by Schema V2.</summary>
internal static class SchemaV2BaselineProvenance
{
    internal const int HandoffManifestVersion = 1;

    internal const string SchemaLabFreezeCommit =
        "5da289582ccf475dfe4c1d7e6dcbf3bb6b4ec4a7";

    internal const string MemoriaNoteBaselineCommit =
        "3ddede8a6240a6abe91f3f431f84a2764ed2faa6";

    internal const string DualIndexSchemaAmendment = "ECLI-1120a";

    internal const string InitialCanonicalDdlSourcePath = "schemas/schema-v2.sql";

    internal const string InitialCanonicalDdlSha256 =
        "e641601efd21623b774b292aed3fe3b64aa1b14c6318de798d0d24e535140ef7";

    internal const string InitialExpectedContractSourcePath =
        "contracts/schema-v2.expected.json";

    internal const string InitialExpectedContractSha256 =
        "63cd171db3b06b63db6b1f3396674fc36a393c53b0d8ed88abfd9a8a778be22f";

    internal const string CanonicalDdlSha256 =
        "a27166b955cff4f0ed4ac8f0f9e6a247f7395e60773173b016b8c49ae6441945";

    internal const string ExpectedContractSha256 =
        "b65fee8786422782f06837720be79d5187e4697101b4205649476ee20b2505c9";
}
