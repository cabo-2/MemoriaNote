using System.Security.Cryptography;
using MemoriaNote.Archive;
using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.NotebookMigration;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Migration;

/// <summary>Verifies schema v1 and CM1 migration through the shared schema v2 sink.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookMigrationServiceTests
{
    /// <summary>Verifies duplicate names are renamed and page-local errors are skipped.</summary>
    [Test]
    public async Task LiveNotebook_AutoRenamesDuplicatesAndPublishesPartialResult()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("migration", "Migration");
        InsertPage(database.DatabasePath, 1, "11111111-1111-4111-8111-111111111111", "Memo", 1);
        InsertPage(database.DatabasePath, 2, "22222222-2222-4222-8222-222222222222", "Memo", 2);
        InsertPage(database.DatabasePath, 3, "33333333-3333-4333-8333-333333333333", "Memo (2)", 1);
        InsertPage(
            database.DatabasePath,
            4,
            "44444444-4444-4444-8444-444444444444",
            "Invalid time",
            1,
            createTime: "2026-01-02 00:00:00",
            updateTime: "2026-01-01 00:00:00");
        var sourceHash = Hash(database.DatabasePath);
        var destination = Path.Combine(database.DirectoryPath, "migrated.mnote");

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(database.DatabasePath, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(result.Report.HasPageErrors, Is.True);
            Assert.That(result.Report.Counts.SourcePages, Is.EqualTo(4));
            Assert.That(result.Report.Counts.MigratedPages, Is.EqualTo(3));
            Assert.That(result.Report.Counts.SkippedPages, Is.EqualTo(1));
            Assert.That(result.Report.Counts.RenamedPages, Is.EqualTo(1));
            Assert.That(
                result.Report.Issues.Any(issue =>
                    issue.Code == NotebookMigrationIssueCode.DuplicateNameRenamed &&
                    issue.FinalName == "Memo (3)"),
                Is.True);
            Assert.That(
                result.Report.Issues.Any(issue =>
                    issue.Code == NotebookMigrationIssueCode.UpdateBeforeCreate &&
                    issue.Severity == NotebookMigrationIssueSeverity.Error),
                Is.True);
            Assert.That(ReadNames(destination), Is.EqualTo(new[] { "Memo", "Memo (2)", "Memo (3)" }));
            Assert.That(Hash(database.DatabasePath), Is.EqualTo(sourceHash));
        }

        var validation = await SchemaV2DatabaseValidator.ValidateAsync(
            destination,
            CancellationToken.None);
        Assert.That(validation.IsValid, Is.True, string.Join(Environment.NewLine, validation.AllIssues));
    }

    /// <summary>Verifies a live notebook and its CM1 backup produce the same logical result.</summary>
    [Test]
    public async Task LiveNotebookAndCm1Archive_ProduceSameLogicalFingerprint()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("pair", "Pair");
        InsertPage(
            database.DatabasePath,
            7,
            "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE",
            "Page",
            1,
            tags: "{\"Dir\":\"notes/reference\",\"review\":null}",
            text: null,
            createTime: "2026-01-02 03:04:05.1",
            updateTime: "2026-01-02 03:04:05.12");
        var archive = Path.Combine(database.DirectoryPath, "source.mnarchive");
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "archive-temp"));
        var backup = new ArchiveV1BackupService(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance),
            temporaryFiles,
            new FixedClock(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)),
            new ArchiveV1Creator("MemoriaNote.Tests", "1.0.0"));
        var backupResult = await backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archive),
            CancellationToken.None);
        Assert.That(backupResult.IsSuccess, Is.True);

        var liveOutput = Path.Combine(database.DirectoryPath, "live.mnote");
        var archiveOutput = Path.Combine(database.DirectoryPath, "archive.mnote");
        var service = new NotebookMigrationService();
        var live = await service.MigrateAsync(
            new NotebookMigrationRequest(database.DatabasePath, liveOutput),
            CancellationToken.None);
        var archived = await service.MigrateAsync(
            new NotebookMigrationRequest(archive, archiveOutput),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(live.IsSuccess, Is.True, live.Error?.ToString());
            Assert.That(archived.IsSuccess, Is.True, archived.Error?.ToString());
            Assert.That(archived.Report.SourceKind, Is.EqualTo(
                NotebookMigrationSourceKind.ContentModel1Archive));
            Assert.That(live.Report.LogicalFingerprint, Is.EqualTo(
                archived.Report.LogicalFingerprint));
            Assert.That(live.Report.Counts, Is.EqualTo(archived.Report.Counts));
            Assert.That(ReadCurrentPages(liveOutput), Is.EqualTo(ReadCurrentPages(archiveOutput)));
        }
    }

    /// <summary>Verifies explicit resolution can recover invalid names, tags, and erased pages.</summary>
    [Test]
    public async Task Resolution_RepairsPageLocalValuesAndApprovesErasedExclusion()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("resolution", "Resolution");
        InsertPage(
            database.DatabasePath,
            1,
            "11111111-1111-4111-8111-111111111111",
            null,
            1,
            tags: "{\"very-long\":\"" + new string('x', 80) + "\"}");
        InsertPage(
            database.DatabasePath,
            2,
            "22222222-2222-4222-8222-222222222222",
            "Erased",
            1,
            isErased: 1);
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
        var destination = Path.Combine(database.DirectoryPath, "resolved.mnote");

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(
                database.DatabasePath,
                destination,
                resolution),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(result.Report.HasPageErrors, Is.False);
            Assert.That(result.Report.Counts.MigratedPages, Is.EqualTo(1));
            Assert.That(result.Report.Counts.SkippedPages, Is.EqualTo(1));
            Assert.That(ReadNames(destination), Is.EqualTo(new[] { "Recovered" }));
            Assert.That(
                result.Report.Issues.Any(issue =>
                    issue.Code == NotebookMigrationIssueCode.ErasedPageExcluded &&
                    issue.Severity == NotebookMigrationIssueSeverity.Warning),
                Is.True);
        }
    }

    /// <summary>Verifies dry-run executes mapping without creating an output or staging directory.</summary>
    [Test]
    public async Task DryRun_ProducesReportWithoutOutput()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("dry", "Dry");
        InsertPage(database.DatabasePath, 1, "11111111-1111-4111-8111-111111111111", "Page", 1);
        var destination = Path.Combine(database.DirectoryPath, "dry.mnote");

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(
                database.DatabasePath,
                destination,
                dryRun: true),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
            Assert.That(result.Report.DryRun, Is.True);
            Assert.That(result.Report.Counts.MigratedPages, Is.EqualTo(1));
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(
                Directory.EnumerateDirectories(database.DirectoryPath, ".mn-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies existing destinations are preserved before the source is migrated.</summary>
    [Test]
    public async Task ExistingDestination_IsPreserved()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("conflict", "Conflict");
        var destination = Path.Combine(database.DirectoryPath, "existing.mnote");
        await File.WriteAllTextAsync(destination, "keep");

        var result = await new NotebookMigrationService().MigrateAsync(
            new NotebookMigrationRequest(database.DatabasePath, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(
                NotebookMigrationErrorCode.DestinationConflict));
            Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("keep"));
        }
    }

    private static void InsertPage(
        string databasePath,
        long rowId,
        string pageId,
        string? name,
        long index,
        string? tags = null,
        string? contentType = "T",
        string createTime = "2026-01-01 00:00:00",
        string updateTime = "2026-01-01 00:00:00",
        long isErased = 0,
        string? text = "body")
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Pages(
                Rowid, Uuid, Name, "Index", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text)
            VALUES(
                $rowId, $pageId, $name, $index, $tags, $contentType,
                $createTime, $updateTime, $isErased, $text);
            """;
        command.Parameters.AddWithValue("$rowId", rowId);
        command.Parameters.AddWithValue("$pageId", pageId);
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
        command.Parameters.AddWithValue("$contentType", (object?)contentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$createTime", createTime);
        command.Parameters.AddWithValue("$updateTime", updateTime);
        command.Parameters.AddWithValue("$isErased", isErased);
        command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        command.ExecuteNonQuery();
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

    private static IReadOnlyList<string> ReadCurrentPages(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PageId || '|' || Name || '|' || CreateTime || '|' ||
                   UpdateTime || '|' || Revision || '|' || Tags || '|' || Text
            FROM Pages
            ORDER BY PageId COLLATE BINARY;
            """;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _value;

        internal FixedClock(DateTimeOffset value)
        {
            _value = value;
        }

        public DateTimeOffset UtcNow => _value;
    }
}
