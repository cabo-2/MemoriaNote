using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoriaNote.Archive;
using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.NotebookMigration;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Migration;

/// <summary>Fixes the CM1 migration compatibility boundary as a regression contract.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ContentModel1MigrationCompatibilityTests
{
    /// <summary>Verifies CM1-only page anomalies publish a validated partial output.</summary>
    [Test]
    public async Task PageLocalAnomalies_PublishValidatedPartialOutput()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = Path.Combine(database.DirectoryPath, "source.mnarchive");
        var destination = Path.Combine(database.DirectoryPath, "partial.mnote");
        var pages = new[]
        {
            Page(1, "11111111-1111-4111-8111-111111111111", "Memo", 1),
            Page(2, "22222222-2222-4222-8222-222222222222", "Memo", 2),
            Page(
                3,
                "33333333-3333-4333-8333-333333333333",
                string.Concat(Enumerable.Repeat("\U0001F600", 256)),
                3),
            Page(
                4,
                "44444444-4444-4444-8444-444444444444",
                "Invalid time",
                4,
                createTime: "2026-01-02 00:00:00",
                updateTime: "2026-01-01 00:00:00"),
            Page(
                5,
                "55555555-5555-4555-8555-555555555555",
                "Invalid tags",
                5,
                tags: "{\"key\":\"" + new string('x', 64) + "\"}"),
            Page(
                6,
                "66666666-6666-4666-8666-666666666666",
                "Erased",
                6,
                isErased: 1)
        };
        await CreateArchiveAsync(source, pages);
        var sourceHash = Hash(source);

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(source, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(result.Report.SourceKind, Is.EqualTo(
                NotebookMigrationSourceKind.ContentModel1Archive));
            Assert.That(result.Report.HasPageErrors, Is.True);
            Assert.That(result.Report.Counts.SourcePages, Is.EqualTo(6));
            Assert.That(result.Report.Counts.MigratedPages, Is.EqualTo(2));
            Assert.That(result.Report.Counts.SkippedPages, Is.EqualTo(4));
            Assert.That(result.Report.Counts.RenamedPages, Is.EqualTo(1));
            Assert.That(ReadNames(destination), Is.EqualTo(new[] { "Memo", "Memo (2)" }));
            Assert.That(Hash(source), Is.EqualTo(sourceHash));
            Assert.That(File.Exists(destination + "-wal"), Is.False);
            Assert.That(File.Exists(destination + "-shm"), Is.False);
            Assert.That(File.Exists(destination + "-journal"), Is.False);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.InvalidPageName &&
                    issue.Reason == NotebookMigrationIssueReason.ScalarLimitExceeded &&
                    issue.Actual == 256 &&
                    issue.Limit == 255));
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.UpdateBeforeCreate &&
                    issue.Severity == NotebookMigrationIssueSeverity.Error));
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.InvalidTags &&
                    issue.Severity == NotebookMigrationIssueSeverity.Error));
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.DuplicateNameRenamed &&
                    issue.Severity == NotebookMigrationIssueSeverity.Warning &&
                    issue.FinalName == "Memo (2)"));
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.ErasedPageSkipped &&
                    issue.Severity == NotebookMigrationIssueSeverity.Error));
        }

        var validation = await SchemaV2DatabaseValidator.ValidateAsync(
            destination,
            CancellationToken.None);
        Assert.That(validation.IsValid, Is.True, string.Join(Environment.NewLine, validation.AllIssues));
    }

    /// <summary>Verifies CM1 resolution repairs pages and explicitly excludes erased pages.</summary>
    [Test]
    public async Task Resolution_RepairsValuesAndApprovesErasedExclusion()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = Path.Combine(database.DirectoryPath, "source.mnarchive");
        var destination = Path.Combine(database.DirectoryPath, "resolved.mnote");
        await CreateArchiveAsync(
            source,
            new[]
            {
                Page(
                    1,
                    "11111111-1111-4111-8111-111111111111",
                    new string('x', 256),
                    1,
                    tags: "{\"very-long\":\"" + new string('x', 80) + "\"}"),
                Page(
                    2,
                    "22222222-2222-4222-8222-222222222222",
                    "Erased",
                    2,
                    isErased: 1)
            });
        var resolution = Path.Combine(database.DirectoryPath, "resolution.json");
        await File.WriteAllTextAsync(
            resolution,
            """
            {
              "version": 1,
              "renames": [
                {
                  "pageId": "11111111-1111-4111-8111-111111111111",
                  "name": "Recovered"
                }
              ],
              "tagOverrides": [
                {
                  "pageId": "11111111-1111-4111-8111-111111111111",
                  "labels": ["legacy"]
                }
              ],
              "erasedPageExclusions": [
                "22222222-2222-4222-8222-222222222222"
              ]
            }
            """);

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(source, destination, resolution),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(result.Report.HasPageErrors, Is.False);
            Assert.That(result.Report.Counts.MigratedPages, Is.EqualTo(1));
            Assert.That(result.Report.Counts.SkippedPages, Is.EqualTo(1));
            Assert.That(result.Report.Counts.TagOverrides, Is.EqualTo(1));
            Assert.That(ReadNames(destination), Is.EqualTo(new[] { "Recovered" }));
            Assert.That(
                result.Report.Issues.Select(issue => issue.Code),
                Does.Contain(NotebookMigrationIssueCode.PageRenamed));
            Assert.That(
                result.Report.Issues.Select(issue => issue.Code),
                Does.Contain(NotebookMigrationIssueCode.TagsOverridden));
            Assert.That(
                result.Report.Issues,
                Has.Some.Matches<NotebookMigrationIssue>(issue =>
                    issue is not null &&
                    issue.Code == NotebookMigrationIssueCode.ErasedPageExcluded &&
                    issue.Severity == NotebookMigrationIssueSeverity.Warning));
        }
    }

    /// <summary>Verifies an invalid CM1 contract is fatal and never publishes output.</summary>
    [Test]
    public async Task ContractViolation_IsFatalAndPublishesNothing()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = Path.Combine(database.DirectoryPath, "invalid.mnarchive");
        var destination = Path.Combine(database.DirectoryPath, "invalid.mnote");
        var invalidMetadata = Encoding.UTF8.GetBytes(
            "[{\"key\":\"Name\",\"value\":\"cm1\"}," +
            "{\"key\":\"Title\",\"value\":\"CM1\"}]\n");
        await CreateRawArchiveAsync(
            source,
            invalidMetadata,
            invalidMetadata,
            metadataRecordCount: 2);
        var sourceHash = Hash(source);

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(source, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(NotebookMigrationErrorCode.InvalidSource));
            Assert.That(result.Report, Is.Null);
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(Hash(source), Is.EqualTo(sourceHash));
            Assert.That(
                Directory.EnumerateDirectories(database.DirectoryPath, ".mn-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies CM1 checksum corruption is fatal and never publishes output.</summary>
    [Test]
    public async Task ChecksumMismatch_IsFatalAndPublishesNothing()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = Path.Combine(database.DirectoryPath, "corrupt.mnarchive");
        var destination = Path.Combine(database.DirectoryPath, "corrupt.mnote");
        var describedMetadata = ValidMetadataBytes();
        var corruptedMetadata = describedMetadata.ToArray();
        var titleOffset = Encoding.UTF8.GetString(corruptedMetadata).IndexOf("CM1", StringComparison.Ordinal);
        corruptedMetadata[titleOffset] = (byte)'D';
        await CreateRawArchiveAsync(
            source,
            describedMetadata,
            corruptedMetadata,
            metadataRecordCount: 3);
        var sourceHash = Hash(source);

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(source, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(NotebookMigrationErrorCode.InvalidSource));
            Assert.That(result.Report, Is.Null);
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(Hash(source), Is.EqualTo(sourceHash));
            Assert.That(
                Directory.EnumerateDirectories(database.DirectoryPath, ".mn-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies a resolution cannot exclude a non-erased CM1 page.</summary>
    [Test]
    public async Task InvalidResolution_IsFatalAndPublishesNothing()
    {
        using var database = new TemporaryNotebookDatabase();
        var source = Path.Combine(database.DirectoryPath, "source.mnarchive");
        var destination = Path.Combine(database.DirectoryPath, "invalid-resolution.mnote");
        await CreateArchiveAsync(
            source,
            new[]
            {
                Page(1, "11111111-1111-4111-8111-111111111111", "Page", 1)
            });
        var resolution = Path.Combine(database.DirectoryPath, "resolution.json");
        await File.WriteAllTextAsync(
            resolution,
            """
            {
              "version": 1,
              "erasedPageExclusions": [
                "11111111-1111-4111-8111-111111111111"
              ]
            }
            """);

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(source, destination, resolution),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(NotebookMigrationErrorCode.InvalidResolution));
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(
                Directory.EnumerateDirectories(database.DirectoryPath, ".mn-*"),
                Is.Empty);
        }
    }

    private static ArchiveV1PageRow Page(
        int rowId,
        string pageId,
        string name,
        int index,
        string tags = "{}",
        string createTime = "2026-01-01 00:00:00",
        string updateTime = "2026-01-01 00:00:00",
        int isErased = 0) =>
        new(
            rowId,
            pageId,
            name,
            index,
            tags,
            "T",
            createTime,
            updateTime,
            isErased,
            "body");

    private static async Task CreateArchiveAsync(
        string path,
        IReadOnlyList<ArchiveV1PageRow> pages)
    {
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote.Tests", "ecli-1145e"),
            new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ArchiveV1MetadataRow("Name", "cm1"),
                new ArchiveV1MetadataRow("Title", "CM1"),
                new ArchiveV1MetadataRow("Version", "1")
            },
            ToAsync(pages));
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await new ArchiveV1Writer().WriteAsync(output, request, CancellationToken.None);
    }

    private static async Task CreateRawArchiveAsync(
        string path,
        byte[] describedMetadata,
        byte[] actualMetadata,
        long metadataRecordCount)
    {
        var emptyPages = Array.Empty<byte>();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "memoria-note-archive",
            archiveFormatVersion = 1,
            contentModelVersion = 1,
            sourceNotebookFormatVersion = "1",
            createdBy = new
            {
                application = "MemoriaNote.Tests",
                version = "ecli-1145e"
            },
            createdAtUtc = "2026-11-05T00:00:00.0000000Z",
            dataSets = new
            {
                metadata = Descriptor(
                    "metadata.json",
                    metadataRecordCount,
                    describedMetadata),
                pages = Descriptor("pages.ndjson", 0, emptyPages)
            }
        });

        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteEntryAsync(archive, "manifest.json", manifest);
        await WriteEntryAsync(archive, "metadata.json", actualMetadata);
        await WriteEntryAsync(archive, "pages.ndjson", emptyPages);
    }

    private static object Descriptor(string entry, long recordCount, byte[] content) =>
        new
        {
            entry,
            recordCount,
            uncompressedByteLength = content.LongLength,
            sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
        };

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        await using var stream = entry.Open();
        await stream.WriteAsync(content);
    }

    private static byte[] ValidMetadataBytes() => Encoding.UTF8.GetBytes(
        "[{\"key\":\"Name\",\"value\":\"cm1\"}," +
        "{\"key\":\"Title\",\"value\":\"CM1\"}," +
        "{\"key\":\"Version\",\"value\":\"1\"}]\n");

    private static async IAsyncEnumerable<ArchiveV1PageRow> ToAsync(
        IEnumerable<ArchiveV1PageRow> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            await Task.Yield();
        }
    }

    private static IReadOnlyList<string> ReadNames(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Pages ORDER BY Name COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    private static string Hash(string path)
    {
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
