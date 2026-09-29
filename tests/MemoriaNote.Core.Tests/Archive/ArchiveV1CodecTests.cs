using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MemoriaNote.Archive;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Verifies the archive v1 codec and validation contract.</summary>
[TestFixture]
public sealed class ArchiveV1CodecTests
{
    static readonly string ExamplesDirectory = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "reference",
        "archive",
        "v1",
        "examples");

    /// <summary>Verifies canonical serialization and streaming round-trip behavior.</summary>
    [Test]
    public async Task Writer_OutputMatchesPublishedExample_AndReaderStreamsAllRows()
    {
        await using var output = new MemoryStream();
        var writer = new ArchiveV1Writer();

        await writer.WriteAsync(output, CreateExampleRequest(), CancellationToken.None);

        using (var archive = OpenArchive(output))
        {
            AssertEntryMatchesExample(archive, "manifest.json");
            AssertEntryMatchesExample(archive, "metadata.json");
            AssertEntryMatchesExample(archive, "pages.ndjson");
        }

        var reader = new ArchiveV1Reader();
        var validation = await reader.ValidateAsync(output, CancellationToken.None);
        var sink = new RecordingSink();
        var read = await reader.ReadAsync(output, sink, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, Describe(validation));
            Assert.That(read.IsValid, Is.True, Describe(read));
            Assert.That(sink.Metadata, Has.Count.EqualTo(10));
            Assert.That(sink.Pages, Has.Count.EqualTo(3));
            Assert.That(sink.Metadata.Single(row => row.Key == "NullableValue").Value, Is.Null);
            Assert.That(sink.Pages[0].Name, Is.EqualTo("日記"));
            Assert.That(sink.Pages[0].Text, Is.EqualTo("1 行目\n2 行目"));
            Assert.That(sink.Pages[1].Text, Is.Empty);
            Assert.That(sink.Pages[2].Text, Is.Null);
        }
    }

    /// <summary>Verifies the published expanded valid fixture.</summary>
    [Test]
    public async Task Reader_AcceptsPublishedValidFixture()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(report.IsValid, Is.True, Describe(report));
    }

    /// <summary>Verifies the canonical empty pages data set.</summary>
    [Test]
    public async Task Codec_EmptyPagesDataSet_RoundTripsAsEmptyEntry()
    {
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote", "test"),
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
            ExampleMetadata(),
            ToAsync(Array.Empty<ArchiveV1PageRow>()));
        await using var output = new MemoryStream();

        await new ArchiveV1Writer().WriteAsync(output, request, CancellationToken.None);
        var report = await new ArchiveV1Reader().ValidateAsync(
            output,
            CancellationToken.None);

        using var archive = OpenArchive(output);
        var pages = archive.GetEntry("pages.ndjson");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsValid, Is.True, Describe(report));
            Assert.That(pages, Is.Not.Null);
            Assert.That(pages!.Length, Is.Zero);
        }
    }

    /// <summary>Verifies that unsupported versions remain distinct from malformed JSON.</summary>
    [Test]
    public async Task Reader_UnsupportedVersion_ReturnsStableIssueCode()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("invalid/manifest-unsupported-archive-version.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.UnsupportedVersion);
        Assert.That(report.Issues, Has.None.Matches<ArchiveV1ValidationIssue>(
            issue => issue.Code == ArchiveV1IssueCode.MalformedJson));
    }

    /// <summary>Verifies semantic validation against a published invalid metadata example.</summary>
    [Test]
    public async Task Reader_DuplicateMetadataKey_IsRejected()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("invalid/metadata-duplicate-key.json"),
            ReadExample("valid/pages.ndjson"));

        var sink = new RecordingSink();
        var report = await new ArchiveV1Reader().ReadAsync(
            archive,
            sink,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.DuplicateMetadataKey);
        Assert.That(sink.Metadata, Is.Empty);
        Assert.That(sink.Pages, Is.Empty);
    }

    /// <summary>Verifies structural validation against a published invalid page example.</summary>
    [Test]
    public async Task Reader_MissingPageProperty_IsRejected()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("invalid/page-missing-text.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.MissingProperty);
        Assert.That(report.Issues.Any(issue => issue.Location.EndsWith(".text")), Is.True);
    }

    /// <summary>Verifies duplicate UUID comparison through parsed GUID values.</summary>
    [Test]
    public async Task Reader_DuplicatePageUuid_IsRejected()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("invalid/pages-duplicate-uuid.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.DuplicatePageUuid);
    }

    /// <summary>Verifies strict JSON property and integer-token handling.</summary>
    [TestCase("duplicate", ArchiveV1IssueCode.DuplicateProperty)]
    [TestCase("unknown", ArchiveV1IssueCode.UnknownProperty)]
    [TestCase("exponent", ArchiveV1IssueCode.InvalidPropertyValue)]
    public async Task Reader_StrictManifestRules_AreEnforced(
        string mutation,
        ArchiveV1IssueCode expected)
    {
        var manifest = Encoding.UTF8.GetString(ReadExample("valid/manifest.json"));
        manifest = mutation switch
        {
            "duplicate" => manifest.Replace(
                "  \"format\": \"memoria-note-archive\",",
                "  \"format\": \"memoria-note-archive\",\n  \"format\": \"memoria-note-archive\","),
            "unknown" => manifest.Replace(
                "  \"format\": \"memoria-note-archive\",",
                "  \"format\": \"memoria-note-archive\",\n  \"future\": true,"),
            "exponent" => manifest.Replace(
                "\"archiveFormatVersion\": 1",
                "\"archiveFormatVersion\": 1e0"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await using var archive = CreateFixtureArchive(
            Encoding.UTF8.GetBytes(manifest),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, expected);
    }

    /// <summary>Verifies that manifest-declared resource excess is rejected before payload work.</summary>
    [TestCase("\"recordCount\": 3", "\"recordCount\": 1000001", ArchiveV1IssueCode.RecordLimitExceeded)]
    [TestCase(
        "\"uncompressedByteLength\": 670",
        "\"uncompressedByteLength\": 8589934593",
        ArchiveV1IssueCode.EntryTooLarge)]
    public async Task Reader_ManifestResourceLimits_AreEnforcedEarly(
        string original,
        string replacement,
        ArchiveV1IssueCode expected)
    {
        var manifest = Encoding.UTF8.GetString(ReadExample("valid/manifest.json"))
            .Replace(original, replacement);
        await using var archive = CreateFixtureArchive(
            Encoding.UTF8.GetBytes(manifest),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, expected);
    }

    /// <summary>Verifies BOM and malformed UTF-8 rejection.</summary>
    [TestCase(true, ArchiveV1IssueCode.ByteOrderMarkNotAllowed)]
    [TestCase(false, ArchiveV1IssueCode.InvalidUtf8)]
    public async Task Reader_InvalidMetadataEncoding_IsRejected(
        bool useBom,
        ArchiveV1IssueCode expected)
    {
        var metadata = useBom
            ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(ReadExample("valid/metadata.json")).ToArray()
            : new byte[] { (byte)'[', (byte)'"', 0xff, (byte)'"', (byte)']' };
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            metadata,
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, expected);
    }

    /// <summary>Verifies exact entry layout enforcement.</summary>
    [Test]
    public async Task Reader_AdditionalEntry_IsRejectedBeforePayloadReading()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"),
            ("extra.json", new byte[] { 0xff }));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.InvalidEntryCount);
        AssertIssue(report, ArchiveV1IssueCode.UnexpectedEntry);
    }

    /// <summary>Verifies missing and duplicate entry rejection.</summary>
    [Test]
    public async Task Reader_MissingAndDuplicateEntries_AreRejected()
    {
        await using var missing = CreateZip(
            ("manifest.json", ReadExample("valid/manifest.json")),
            ("metadata.json", ReadExample("valid/metadata.json")));
        await using var duplicate = CreateZip(
            ("manifest.json", ReadExample("valid/manifest.json")),
            ("metadata.json", ReadExample("valid/metadata.json")),
            ("metadata.json", ReadExample("valid/metadata.json")),
            ("pages.ndjson", ReadExample("valid/pages.ndjson")));
        var reader = new ArchiveV1Reader();

        var missingReport = await reader.ValidateAsync(missing, CancellationToken.None);
        var duplicateReport = await reader.ValidateAsync(duplicate, CancellationToken.None);

        AssertIssue(missingReport, ArchiveV1IssueCode.MissingEntry);
        AssertIssue(duplicateReport, ArchiveV1IssueCode.DuplicateEntry);
    }

    /// <summary>Verifies declared ZIP compression-ratio screening.</summary>
    [Test]
    public async Task Reader_ExcessiveCompressionRatio_IsRejectedBeforeJsonParsing()
    {
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            new byte[8 * 1024 * 1024],
            Array.Empty<byte>());

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.CompressionRatioExceeded);
        Assert.That(report.Issues, Has.None.Matches<ArchiveV1ValidationIssue>(
            issue => issue.Code == ArchiveV1IssueCode.MalformedJson));
    }

    /// <summary>Verifies classification without interpreting legacy payload bytes.</summary>
    [Test]
    public async Task Reader_LegacyCandidate_IsClassifiedWithoutReadingPayload()
    {
        await using var archive = CreateZip(
            ("metadata.json", new byte[] { 0xff }),
            ("001.json", new byte[] { 0xff }));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                report.Classification,
                Is.EqualTo(ArchiveV1Classification.LegacyArchiveCandidate));
            AssertIssue(report, ArchiveV1IssueCode.LegacyArchiveUnsupported);
            Assert.That(report.Issues, Has.None.Matches<ArchiveV1ValidationIssue>(
                issue => issue.Code == ArchiveV1IssueCode.MalformedJson));
        }
    }

    /// <summary>Verifies that unrelated and malformed ZIP inputs remain distinguishable.</summary>
    [Test]
    public async Task Reader_UnrecognizedAndMalformedInputs_HaveDistinctClassifications()
    {
        await using var unrelated = CreateZip(("readme.txt", Encoding.UTF8.GetBytes("hello")));
        await using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));
        var reader = new ArchiveV1Reader();

        var unrelatedReport = await reader.ValidateAsync(unrelated, CancellationToken.None);
        var malformedReport = await reader.ValidateAsync(malformed, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unrelatedReport.Classification,
                Is.EqualTo(ArchiveV1Classification.UnrecognizedZip));
            Assert.That(
                malformedReport.Classification,
                Is.EqualTo(ArchiveV1Classification.MalformedZip));
        }
    }

    /// <summary>Verifies checksum validation over exact expanded bytes.</summary>
    [Test]
    public async Task Reader_ChangedPayloadWithSameLength_HasChecksumMismatch()
    {
        var metadata = ReadExample("valid/metadata.json");
        var text = Encoding.UTF8.GetString(metadata).Replace("Example Author", "Example Authos");
        await using var archive = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            Encoding.UTF8.GetBytes(text),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, ArchiveV1IssueCode.ChecksumMismatch);
        Assert.That(report.Issues, Has.None.Matches<ArchiveV1ValidationIssue>(
            issue => issue.Code == ArchiveV1IssueCode.ByteLengthMismatch));
    }

    /// <summary>Verifies manifest count and byte-length comparisons.</summary>
    [TestCase("\"recordCount\": 3", "\"recordCount\": 4", ArchiveV1IssueCode.RecordCountMismatch)]
    [TestCase(
        "\"uncompressedByteLength\": 670",
        "\"uncompressedByteLength\": 671",
        ArchiveV1IssueCode.ByteLengthMismatch)]
    public async Task Reader_ManifestPayloadMeasurements_MustMatch(
        string original,
        string replacement,
        ArchiveV1IssueCode expected)
    {
        var manifest = Encoding.UTF8.GetString(ReadExample("valid/manifest.json"))
            .Replace(original, replacement);
        await using var archive = CreateFixtureArchive(
            Encoding.UTF8.GetBytes(manifest),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        AssertIssue(report, expected);
    }

    /// <summary>Verifies cancellation propagation through public streaming operations.</summary>
    [Test]
    public void Codec_PreCanceledToken_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var input = CreateFixtureArchive(
            ReadExample("valid/manifest.json"),
            ReadExample("valid/metadata.json"),
            ReadExample("valid/pages.ndjson"));
        using var output = new MemoryStream();

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ArchiveV1Reader().ValidateAsync(input, cancellation.Token));
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ArchiveV1Writer().WriteAsync(output, CreateExampleRequest(), cancellation.Token));
    }

    /// <summary>Verifies cancellation after streaming has started.</summary>
    [Test]
    public void Writer_MidStreamCancellation_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote", "test"),
            DateTimeOffset.UtcNow,
            ExampleMetadata(),
            CancelAfterFirstPage(ExamplePages(), cancellation));
        using var output = new MemoryStream();

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ArchiveV1Writer().WriteAsync(output, request, cancellation.Token));
    }

    /// <summary>Verifies writer-side validation before invalid records are published.</summary>
    [Test]
    public void Writer_NonCanonicalPageOrder_ReturnsStableValidationIssue()
    {
        var pages = ExamplePages().Reverse().ToArray();
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote", "test"),
            DateTimeOffset.UtcNow,
            ExampleMetadata(),
            ToAsync(pages));
        using var output = new MemoryStream();

        var exception = Assert.ThrowsAsync<ArchiveV1ValidationException>(() =>
            new ArchiveV1Writer().WriteAsync(output, request, CancellationToken.None));

        Assert.That(
            exception!.Report.Issues.Any(issue =>
                issue.Code == ArchiveV1IssueCode.NonCanonicalOrder),
            Is.True);
    }

    static ArchiveV1WriteRequest CreateExampleRequest()
    {
        return new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote", "0.0.0-example"),
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
            ExampleMetadata(),
            ToAsync(ExamplePages()));
    }

    static ArchiveV1MetadataRow[] ExampleMetadata()
    {
        return new[]
        {
            new ArchiveV1MetadataRow("Version", "1"),
            new ArchiveV1MetadataRow("Title", "Archive v1 例"),
            new ArchiveV1MetadataRow("Author", "Example Author"),
            new ArchiveV1MetadataRow("Name", "example"),
            new ArchiveV1MetadataRow("Description", "Archive v1 example"),
            new ArchiveV1MetadataRow("CreateTime", "20260928090000"),
            new ArchiveV1MetadataRow("ReadOnly", "False"),
            new ArchiveV1MetadataRow("Tag", "fixture"),
            new ArchiveV1MetadataRow("NullableValue", null),
            new ArchiveV1MetadataRow("X-Unknown", "preserved")
        };
    }

    static ArchiveV1PageRow[] ExamplePages()
    {
        return new[]
        {
            new ArchiveV1PageRow(
                1,
                "11111111-1111-4111-8111-111111111111",
                "日記",
                1,
                "{\"Dir\":\"journal/2026\"}",
                "T",
                "2026-09-28 09:00:00",
                "2026-09-28 09:01:02.1234567",
                0,
                "1 行目\n2 行目"),
            new ArchiveV1PageRow(
                2,
                "22222222-2222-4222-8222-222222222222",
                "Empty",
                1,
                "{}",
                "",
                "2026-09-28 10:00:00",
                "2026-09-28 10:00:00",
                0,
                ""),
            new ArchiveV1PageRow(
                3,
                "33333333-3333-4333-8333-333333333333",
                null,
                1,
                null,
                null,
                "2026-09-28 11:00:00",
                "2026-09-28 11:00:00.1",
                1,
                null)
        };
    }

    static async IAsyncEnumerable<ArchiveV1PageRow> ToAsync(
        IEnumerable<ArchiveV1PageRow> pages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
            await Task.Yield();
        }
    }

    static async IAsyncEnumerable<ArchiveV1PageRow> CancelAfterFirstPage(
        IEnumerable<ArchiveV1PageRow> pages,
        CancellationTokenSource cancellation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
            cancellation.Cancel();
            await Task.Yield();
        }
    }

    static MemoryStream CreateFixtureArchive(
        byte[] manifest,
        byte[] metadata,
        byte[] pages,
        params (string Name, byte[] Bytes)[] additionalEntries)
    {
        var entries = new List<(string Name, byte[] Bytes)>
        {
            ("metadata.json", metadata),
            ("pages.ndjson", pages),
            ("manifest.json", manifest)
        };
        entries.AddRange(additionalEntries);
        return CreateZip(entries.ToArray());
    }

    static MemoryStream CreateZip(params (string Name, byte[] Bytes)[] entries)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(item.Bytes);
            }
        }
        output.Position = 0;
        return output;
    }

    static ZipArchive OpenArchive(MemoryStream stream)
    {
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
    }

    static void AssertEntryMatchesExample(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null);
        using var input = entry!.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        Assert.That(output.ToArray(), Is.EqualTo(ReadExample("valid/" + entryName)));
    }

    static byte[] ReadExample(string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(
            ExamplesDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    static void AssertIssue(ArchiveV1ValidationReport report, ArchiveV1IssueCode code)
    {
        Assert.That(
            report.Issues.Any(issue => issue.Code == code),
            Is.True,
            Describe(report));
    }

    static string Describe(ArchiveV1ValidationReport report)
    {
        return string.Join(
            Environment.NewLine,
            report.Issues.Select(issue => $"{issue.Code}: {issue.Location}"));
    }

    sealed class RecordingSink : IArchiveV1RecordSink
    {
        internal List<ArchiveV1MetadataRow> Metadata { get; } = new();
        internal List<ArchiveV1PageRow> Pages { get; } = new();

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Metadata.Add(row);
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePageAsync(
            ArchiveV1PageRow row,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pages.Add(row);
            return ValueTask.CompletedTask;
        }
    }
}
