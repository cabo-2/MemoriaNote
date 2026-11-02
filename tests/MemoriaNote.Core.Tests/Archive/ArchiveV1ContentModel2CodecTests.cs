using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoriaNote.Archive;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Locks the content model 2 codec to the ECLI-1130a reference bytes.</summary>
[TestFixture]
[Category("Functional")]
public sealed class ArchiveV1ContentModel2CodecTests
{
    private static readonly string ExamplesDirectory = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "reference",
        "archive",
        "v1-content-model-v2",
        "examples");

    /// <summary>Verifies every immutable compatibility fixture is accepted and streamed.</summary>
    [TestCase("empty", 3, 0)]
    [TestCase("single-page", 4, 1)]
    [TestCase("full-field", 8, 3)]
    public async Task CompatibilityFixture_IsAccepted(
        string fixture,
        int metadataCount,
        int pageCount)
    {
        await using var archive = CreateFixtureArchive("compatibility", fixture);
        var sink = new RecordingSink();

        var report = await new ArchiveV1ContentModel2Reader().ReadAsync(
            archive,
            sink,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsValid, Is.True, Describe(report));
            Assert.That(sink.Metadata, Has.Count.EqualTo(metadataCount));
            Assert.That(sink.Pages, Has.Count.EqualTo(pageCount));
        }
    }

    /// <summary>Verifies canonical writer payload bytes equal the full-field reference fixture.</summary>
    [Test]
    public async Task FullFieldFixture_MatchesCanonicalWriterPayloadBytes()
    {
        var metadata = ReadMetadata("compatibility/full-field/metadata.json");
        var pages = ReadPages("compatibility/full-field/pages.ndjson");
        var request = new ArchiveV1ContentModel2WriteRequest(
            new ArchiveV1Creator("MemoriaNote.Tests", "ecli-1130a"),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            metadata,
            ToAsync(pages));
        await using var output = new MemoryStream();

        await new ArchiveV1ContentModel2Writer().WriteAsync(
            output,
            request,
            CancellationToken.None);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entryName in new[] { "manifest.json", "metadata.json", "pages.ndjson" })
        {
            var entry = archive.GetEntry(entryName);
            Assert.That(entry, Is.Not.Null);
            await using var input = entry!.Open();
            using var actual = new MemoryStream();
            await input.CopyToAsync(actual);
            Assert.That(
                actual.ToArray(),
                Is.EqualTo(ReadBytes($"compatibility/full-field/{entryName}")),
                entryName);
        }
    }

    /// <summary>Verifies every ECLI-1130a invalid JSON fixture reaches its intended rejection.</summary>
    [TestCase("manifest-checksum-mismatch.json", "manifest", ArchiveV1IssueCode.ChecksumMismatch)]
    [TestCase("manifest-mixed-source-version.json", "manifest", ArchiveV1IssueCode.UnsupportedVersion)]
    [TestCase("manifest-unsupported-content-model-version.json", "manifest", ArchiveV1IssueCode.UnsupportedVersion)]
    [TestCase("metadata-duplicate-key.json", "metadata", ArchiveV1IssueCode.DuplicateMetadataKey)]
    [TestCase("page-invalid-timestamp.ndjson", "pages", ArchiveV1IssueCode.InvalidPropertyValue)]
    [TestCase("page-missing-text.ndjson", "pages", ArchiveV1IssueCode.MissingProperty)]
    [TestCase("page-noncanonical-page-id.ndjson", "pages", ArchiveV1IssueCode.InvalidPropertyValue)]
    [TestCase("page-noncanonical-tags.ndjson", "pages", ArchiveV1IssueCode.InvalidPropertyValue)]
    [TestCase("pages-duplicate-page-id.ndjson", "pages", ArchiveV1IssueCode.DuplicatePageId)]
    public async Task InvalidFixture_IsRejected(
        string fileName,
        string replacement,
        ArchiveV1IssueCode expected)
    {
        var manifest = replacement == "manifest"
            ? ReadBytes("invalid/" + fileName)
            : null;
        var metadata = replacement == "metadata"
            ? ReadBytes("invalid/" + fileName)
            : ReadBytes("valid/metadata.json");
        var pages = replacement == "pages"
            ? ReadBytes("invalid/" + fileName)
            : ReadBytes("valid/pages.ndjson");
        manifest ??= CreateManifest(metadata, pages);
        await using var archive = CreateArchive(manifest, metadata, pages);

        var report = await new ArchiveV1ContentModel2Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue => issue.Code == expected),
            Is.True,
            Describe(report));
    }

    /// <summary>Verifies the published CM1 codec does not reinterpret content model 2.</summary>
    [Test]
    public async Task ContentModel1Reader_RejectsContentModel2AsUnsupportedVersion()
    {
        await using var archive = CreateFixtureArchive("compatibility", "single-page");

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue => issue.Code == ArchiveV1IssueCode.UnsupportedVersion),
            Is.True,
            Describe(report));
    }

    /// <summary>Verifies the native CM2 reader does not reinterpret published CM1 payloads.</summary>
    [Test]
    public async Task ContentModel2Reader_RejectsContentModel1AsUnsupportedVersion()
    {
        var cm1Directory = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "reference",
            "archive",
            "v1",
            "examples",
            "compatibility",
            "full-field");
        await using var archive = CreateArchive(
            File.ReadAllBytes(Path.Combine(cm1Directory, "manifest.json")),
            File.ReadAllBytes(Path.Combine(cm1Directory, "metadata.json")),
            File.ReadAllBytes(Path.Combine(cm1Directory, "pages.ndjson")));

        var report = await new ArchiveV1ContentModel2Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue => issue.Code == ArchiveV1IssueCode.UnsupportedVersion),
            Is.True,
            Describe(report));
    }

    /// <summary>Verifies a non-object manifest is classified instead of escaping as an exception.</summary>
    [Test]
    public async Task NonObjectManifest_ReturnsInvalidPropertyType()
    {
        await using var archive = CreateArchive(
            Encoding.UTF8.GetBytes("[]\n"),
            ReadBytes("valid/metadata.json"),
            ReadBytes("valid/pages.ndjson"));

        var report = await new ArchiveV1ContentModel2Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue => issue.Code == ArchiveV1IssueCode.InvalidPropertyType),
            Is.True,
            Describe(report));
    }

    private static MemoryStream CreateFixtureArchive(string group, string fixture) =>
        CreateArchive(
            ReadBytes($"{group}/{fixture}/manifest.json"),
            ReadBytes($"{group}/{fixture}/metadata.json"),
            ReadBytes($"{group}/{fixture}/pages.ndjson"));

    private static MemoryStream CreateArchive(byte[] manifest, byte[] metadata, byte[] pages)
    {
        var result = new MemoryStream();
        using (var archive = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", manifest);
            WriteEntry(archive, "metadata.json", metadata);
            WriteEntry(archive, "pages.ndjson", pages);
        }
        result.Position = 0;
        return result;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static byte[] CreateManifest(byte[] metadata, byte[] pages)
    {
        var metadataCount = JsonDocument.Parse(metadata).RootElement.GetArrayLength();
        var pageCount = pages.Count(value => value == (byte)'\n');
        var json = $$"""
            {
              "format": "memoria-note-archive",
              "archiveFormatVersion": 1,
              "contentModelVersion": 2,
              "sourceNotebookFormatVersion": "2",
              "createdBy": {
                "application": "MemoriaNote.Tests",
                "version": "ecli-1130b"
              },
              "createdAtUtc": "2026-11-01T00:00:00.0000000Z",
              "dataSets": {
                "metadata": {
                  "entry": "metadata.json",
                  "recordCount": {{metadataCount}},
                  "uncompressedByteLength": {{metadata.LongLength}},
                  "sha256": "{{Sha(metadata)}}"
                },
                "pages": {
                  "entry": "pages.ndjson",
                  "recordCount": {{pageCount}},
                  "uncompressedByteLength": {{pages.LongLength}},
                  "sha256": "{{Sha(pages)}}"
                }
              }
            }
            """ + "\n";
        return Encoding.UTF8.GetBytes(json);
    }

    private static IReadOnlyList<ArchiveV1MetadataRow> ReadMetadata(string relativePath)
    {
        using var document = JsonDocument.Parse(ReadBytes(relativePath));
        return document.RootElement.EnumerateArray()
            .Select(element => new ArchiveV1MetadataRow(
                element.GetProperty("key").GetString()!,
                element.GetProperty("value").ValueKind == JsonValueKind.Null
                    ? null
                    : element.GetProperty("value").GetString()))
            .ToArray();
    }

    private static IReadOnlyList<ArchiveV1ContentModel2PageRow> ReadPages(string relativePath)
    {
        return File.ReadLines(Path.Combine(ExamplesDirectory, relativePath))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                return new ArchiveV1ContentModel2PageRow(
                    root.GetProperty("sourceInternalId").GetInt64(),
                    root.GetProperty("pageId").GetString()!,
                    root.GetProperty("name").GetString()!,
                    root.GetProperty("createTime").GetString()!,
                    root.GetProperty("updateTime").GetString()!,
                    root.GetProperty("revision").GetInt64(),
                    root.GetProperty("tags").GetString()!,
                    root.GetProperty("text").GetString()!);
            })
            .ToArray();
    }

    private static byte[] ReadBytes(string relativePath) =>
        File.ReadAllBytes(Path.Combine(ExamplesDirectory, relativePath));

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Describe(ArchiveV1ValidationReport report) =>
        string.Join(", ", report.Issues.Select(issue => $"{issue.Code}:{issue.Location}"));

    private static async IAsyncEnumerable<T> ToAsync<T>(
        IEnumerable<T> rows,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private sealed class RecordingSink : IArchiveV1ContentModel2RecordSink
    {
        internal List<ArchiveV1MetadataRow> Metadata { get; } = new();

        internal List<ArchiveV1ContentModel2PageRow> Pages { get; } = new();

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken)
        {
            Metadata.Add(row);
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePageAsync(
            ArchiveV1ContentModel2PageRow row,
            CancellationToken cancellationToken)
        {
            Pages.Add(row);
            return ValueTask.CompletedTask;
        }
    }
}
