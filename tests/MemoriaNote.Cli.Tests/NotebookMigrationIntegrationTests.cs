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
}
