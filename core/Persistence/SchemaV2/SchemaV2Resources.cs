using System;
using System.IO;
using System.Text;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Provides access to fixed Schema V2 resources.</summary>
internal static class SchemaV2Resources
{
    internal const string CanonicalDdlResourceName =
        "MemoriaNote.Persistence.SchemaV2.Sql.schema-v2.sql";

    internal const string ExpectedContractResourceName =
        "MemoriaNote.Persistence.SchemaV2.Contracts.schema-v2.expected.json";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static Stream OpenCanonicalDdl()
    {
        return Open(CanonicalDdlResourceName);
    }

    internal static Stream OpenExpectedContract()
    {
        return Open(ExpectedContractResourceName);
    }

    internal static string ReadCanonicalDdl()
    {
        using var stream = OpenCanonicalDdl();
        using var reader = new StreamReader(
            stream,
            StrictUtf8,
            detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static Stream Open(string resourceName)
    {
        return typeof(SchemaV2Resources).Assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"The embedded Schema V2 resource '{resourceName}' was not found.");
    }
}
