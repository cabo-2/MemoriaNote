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

    internal const string MetadataContractAmendment = "ECLI-1145a";

    internal const string ReleaseSearchAmendment = "ECLI-1180";

    internal const string InitialCanonicalDdlSourcePath = "schemas/schema-v2.sql";

    internal const string InitialCanonicalDdlSha256 =
        "e641601efd21623b774b292aed3fe3b64aa1b14c6318de798d0d24e535140ef7";

    internal const string InitialExpectedContractSourcePath =
        "contracts/schema-v2.expected.json";

    internal const string InitialExpectedContractSha256 =
        "63cd171db3b06b63db6b1f3396674fc36a393c53b0d8ed88abfd9a8a778be22f";

    internal const string CanonicalDdlSha256 =
        "070a59ea2ce61f4cf128124014bc7cac9871bcf881833bce401bdb8ae120d97e";

    internal const string ExpectedContractSha256 =
        "ebc1d4aead547285e10ada49cffdd9fb391f0585c18c2bdc79092a2235c34a25";
}
