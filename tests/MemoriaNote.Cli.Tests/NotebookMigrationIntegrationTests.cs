using MemoriaNote.Archive;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies schema v1 migration through the production CLI composition.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class NotebookMigrationIntegrationTests
{
    /// <summary>Verifies warning is the default report level and info entries remain hidden.</summary>
    [Test]
    public async Task Migrate_DefaultReportLevel_WritesWarningsAndSummary()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        InsertPage(source, 1, "11111111-1111-4111-8111-111111111111", "Memo", 1);
        InsertPage(source, 2, "22222222-2222-4222-8222-222222222222", "Memo", 2);
        var destination = Path.Combine(harness.WorkingDirectory, "migrated.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain(
                "WARNING duplicate-name-renamed"));
            Assert.That(result.StandardOutput, Does.Not.Contain(
                "INFO indexes-dropped"));
            Assert.That(result.StandardOutput, Does.Contain(
                "source-pages=2, migrated=2, skipped=0, renamed=1"));
            Assert.That(File.Exists(destination), Is.True);
        }
    }

    /// <summary>Verifies unknown metadata reports keys but never stored values.</summary>
    [Test]
    public async Task Migrate_UnknownMetadata_WritesOneValueFreeWarning()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        SetMetadata(source, "zeta-extension", "private-zeta-value");
        SetMetadata(source, "Alpha-extension", "private-alpha-value");
        var destination = Path.Combine(harness.WorkingDirectory, "migrated.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain(
                "WARNING unknown-metadata-dropped field=\"metadata\" count=2 " +
                "keys=[\"Alpha-extension\",\"zeta-extension\"]"));
            Assert.That(result.StandardOutput, Does.Not.Contain("private-alpha-value"));
            Assert.That(result.StandardOutput, Does.Not.Contain("private-zeta-value"));
            Assert.That(
                ReadMetadataKeys(destination),
                Is.EqualTo(new[]
                {
                    "Author",
                    "CreateTime",
                    "Description",
                    "ReadOnly",
                    "Tag",
                    "Title"
                }));
        }
    }

    /// <summary>Verifies the error report level hides warnings while retaining the summary.</summary>
    [Test]
    public async Task Migrate_ErrorReportLevel_HidesWarnings()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        InsertPage(source, 1, "11111111-1111-4111-8111-111111111111", "Memo", 1);
        InsertPage(source, 2, "22222222-2222-4222-8222-222222222222", "Memo", 2);

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            Path.Combine(harness.WorkingDirectory, "migrated.mnote"),
            "--report-level",
            "error");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Not.Contain("WARNING"));
            Assert.That(result.StandardOutput, Does.Contain("renamed=1"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies page-local errors publish a valid partial result and return exit code 6.</summary>
    [Test]
    public async Task Migrate_PageError_ReturnsPartialExitCodeWithoutStderrFailure()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        InsertPage(source, 1, "11111111-1111-4111-8111-111111111111", "Valid", 1);
        InsertPage(
            source,
            2,
            "22222222-2222-4222-8222-222222222222",
            "Invalid",
            1,
            createTime: "2026-01-02 00:00:00",
            updateTime: "2026-01-01 00:00:00");
        var destination = Path.Combine(harness.WorkingDirectory, "partial.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain(
                "ERROR update-before-create"));
            Assert.That(result.StandardOutput, Does.Contain(
                "Migration completed with page errors"));
            Assert.That(result.StandardOutput, Does.Contain(
                "source-pages=2, migrated=1, skipped=1"));
            Assert.That(File.Exists(destination), Is.True);
            Assert.That(ReadPageCount(destination), Is.EqualTo(1));
        }
    }

    /// <summary>Verifies a page-name failure reports its stable reason and boundary values.</summary>
    [Test]
    public async Task Migrate_PageNameOverLimit_ReportsReasonAndBoundary()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        InsertPage(
            source,
            1,
            "11111111-1111-4111-8111-111111111111",
            new string('x', 256),
            1);
        var destination = Path.Combine(harness.WorkingDirectory, "partial.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain(
                "ERROR invalid-page-name"));
            Assert.That(result.StandardOutput, Does.Contain(
                "reason=\"scalar-limit-exceeded\" actual=256 limit=255"));
            Assert.That(ReadPageCount(destination), Is.Zero);
        }
    }

    /// <summary>Verifies CM1 page errors use the public partial-success contract.</summary>
    [Test]
    public async Task Migrate_Cm1PageErrors_ReturnsPartialExitCodeAndReport()
    {
        using var harness = new CliInProcessHarness();
        var source = Path.Combine(harness.WorkingDirectory, "source.mnarchive");
        await CreateCm1ArchiveAsync(
            source,
            new[]
            {
                Cm1Page(1, "11111111-1111-4111-8111-111111111111", "Valid", 1),
                Cm1Page(
                    2,
                    "22222222-2222-4222-8222-222222222222",
                    "Invalid",
                    2,
                    createTime: "2026-01-02 00:00:00",
                    updateTime: "2026-01-01 00:00:00")
            });
        var destination = Path.Combine(harness.WorkingDirectory, "partial.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain(
                "ERROR update-before-create"));
            Assert.That(result.StandardOutput, Does.Contain(
                "Migration completed with page errors"));
            Assert.That(result.StandardOutput, Does.Contain(
                "source-pages=2, migrated=1, skipped=1"));
            Assert.That(File.Exists(destination), Is.True);
            Assert.That(ReadPageCount(destination), Is.EqualTo(1));
        }
    }

    /// <summary>Verifies CM1 issue filtering and the unconditional summary contract.</summary>
    [TestCase("error", false, false)]
    [TestCase("warning", true, false)]
    [TestCase("info", true, true)]
    public async Task Migrate_Cm1ReportLevel_FiltersIssuesAndWritesSummary(
        string reportLevel,
        bool includesWarning,
        bool includesInfo)
    {
        using var harness = new CliInProcessHarness();
        var source = Path.Combine(harness.WorkingDirectory, "source.mnarchive");
        await CreateCm1ArchiveAsync(
            source,
            new[]
            {
                Cm1Page(1, "11111111-1111-4111-8111-111111111111", "Memo", 1),
                Cm1Page(2, "22222222-2222-4222-8222-222222222222", "Memo", 2)
            });

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            Path.Combine(harness.WorkingDirectory, "dry-run.mnote"),
            "--dry-run",
            "--report-level",
            reportLevel);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                result.StandardOutput.Contains(
                    "WARNING duplicate-name-renamed",
                    StringComparison.Ordinal),
                Is.EqualTo(includesWarning));
            Assert.That(
                result.StandardOutput.Contains(
                    "INFO indexes-dropped",
                    StringComparison.Ordinal),
                Is.EqualTo(includesInfo));
            Assert.That(result.StandardOutput, Does.Contain("Migration validated"));
            Assert.That(result.StandardOutput, Does.Contain(
                "source-pages=2, migrated=2, skipped=0, renamed=1"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies dry-run validates the output path but creates no database.</summary>
    [Test]
    public async Task Migrate_DryRun_CreatesNoOutput()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        InsertPage(source, 1, "11111111-1111-4111-8111-111111111111", "Page", 1);
        var destination = Path.Combine(harness.WorkingDirectory, "dry.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination,
            "--dry-run",
            "--report-level",
            "info");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Migration validated"));
            Assert.That(result.StandardOutput, Does.Contain("INFO indexes-dropped"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(destination), Is.False);
        }
    }

    /// <summary>Verifies invalid report levels fail before migration creates output.</summary>
    [Test]
    public async Task Migrate_InvalidReportLevel_ReturnsValidationFailure()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        var destination = Path.Combine(harness.WorkingDirectory, "migrated.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination,
            "--report-level",
            "verbose");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("error, warning, or info"));
            Assert.That(File.Exists(destination), Is.False);
        }
    }

    /// <summary>Verifies numeric enum values are not part of the report-level contract.</summary>
    [Test]
    public async Task Migrate_NumericReportLevel_ReturnsValidationFailure()
    {
        using var harness = new CliInProcessHarness();
        var source = await CreateLegacySourceAsync(harness, "legacy.mnote");
        var destination = Path.Combine(harness.WorkingDirectory, "migrated.mnote");

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            source,
            "--output",
            destination,
            "--report-level",
            "1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("error, warning, or info"));
            Assert.That(File.Exists(destination), Is.False);
        }
    }

    private static async Task<string> CreateLegacySourceAsync(
        CliInProcessHarness harness,
        string name)
    {
        var created = await harness.RunAsync("create", name);
        Assert.That(created.ExitCode, Is.Zero, created.StandardError);
        return Path.Combine(harness.WorkingDirectory, name);
    }

    private static async Task CreateCm1ArchiveAsync(
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

    private static ArchiveV1PageRow Cm1Page(
        int rowId,
        string pageId,
        string name,
        int index,
        string createTime = "2026-01-01 00:00:00",
        string updateTime = "2026-01-01 00:00:00") =>
        new(
            rowId,
            pageId,
            name,
            index,
            "{}",
            "T",
            createTime,
            updateTime,
            0,
            "body");

    private static async IAsyncEnumerable<ArchiveV1PageRow> ToAsync(
        IEnumerable<ArchiveV1PageRow> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            await Task.Yield();
        }
    }

    private static void InsertPage(
        string databasePath,
        long rowId,
        string pageId,
        string name,
        long index,
        string createTime = "2026-01-01 00:00:00",
        string updateTime = "2026-01-01 00:00:00")
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Pages(
                Rowid, Uuid, Name, "Index", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text)
            VALUES(
                $rowId, $pageId, $name, $index, NULL, 'T',
                $createTime, $updateTime, 0, 'body');
            """;
        command.Parameters.AddWithValue("$rowId", rowId);
        command.Parameters.AddWithValue("$pageId", pageId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$createTime", createTime);
        command.Parameters.AddWithValue("$updateTime", updateTime);
        command.ExecuteNonQuery();
    }

    private static long ReadPageCount(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Pages;";
        return (long)command.ExecuteScalar()!;
    }

    private static void SetMetadata(string databasePath, string key, string value)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Metadata(Key, Value) VALUES($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadMetadataKeys(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key FROM Metadata ORDER BY Key COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var keys = new List<string>();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }
}
