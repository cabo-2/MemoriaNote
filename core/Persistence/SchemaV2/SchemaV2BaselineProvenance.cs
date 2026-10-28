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

    internal const string ConnectionFactorySourcePath =
        "src/MemoriaNote.SchemaLab.Core/SqliteConnectionFactory.cs";

    internal const string ConnectionFactorySourceSha256 =
        "0b2e33f199dc1fad5b48a55841a806d7f551fb3dea30ceee7c94b66537a9befe";

    internal const string DatabaseFilesSourcePath =
        "src/MemoriaNote.SchemaLab.Core/SqliteDatabaseFiles.cs";

    internal const string DatabaseFilesSourceSha256 =
        "b9ec57def9b8623ceb5410f3fa147d5640f1bcb03f17aee2254a51fd1655e73b";

    internal const string PendingOutputSourcePath =
        "src/MemoriaNote.SchemaLab.Core/PendingSqliteOutput.cs";

    internal const string PendingOutputSourceSha256 =
        "0eee62991898ef990cb922eff8fda78e4bdafe591ed911cc159854d66070fb08";

    internal const string DatabaseFactorySourcePath =
        "src/MemoriaNote.SchemaLab.Core/SchemaV2DatabaseFactory.cs";

    internal const string DatabaseFactorySourceSha256 =
        "30be0d906bdc583cf740c4f962f68d34123455f053291b6e90383e907a5a15c0";
}
