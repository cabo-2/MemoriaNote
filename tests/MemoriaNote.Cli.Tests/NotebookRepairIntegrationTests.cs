using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook repair through the production CLI composition.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class NotebookRepairIntegrationTests
{
    /// <summary>Verifies help fixes the direct-path, required-output command contract.</summary>
    [Test]
    public async Task Help_DescribesSafeNewOutputRepair()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("notebooks", "repair", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput,
                Does.Contain(
                    "Usage: mn notebooks repair <source> --output <notebook> [options]"));
            Assert.That(result.StandardOutput, Does.Contain("--output <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("authoritative data"));
            Assert.That(result.StandardOutput, Does.Contain("workspace selection"));
            Assert.That(result.StandardOutput, Does.Not.Contain("--force"));
            Assert.That(result.StandardOutput, Does.Not.Contain("--dry-run"));
            Assert.That(result.StandardOutput, Does.Not.Contain("--json"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies derived defects are rebuilt without changing source or workspace.</summary>
    [Test]
    public async Task Repair_DerivedDefect_PublishesValidatedOutputAndReport()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "source")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "source")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "source.mnote");
        var output = Path.Combine(harness.WorkingDirectory, "repaired.mnote");
        await ExecuteAsync(
            source,
            """
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ('11111111-2222-4333-8444-555555555555', 'page',
                    '2026-11-01 00:00:00.0000000', '2026-11-01 00:00:01.0000000',
                    3, '["tag"]', 'private body');
            DELETE FROM PageTags;
            DROP TRIGGER Pages_TagsUpdate;
            """);
        var configurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var beforeSource = await File.ReadAllBytesAsync(source);
        var beforeConfiguration = await File.ReadAllBytesAsync(configurationPath);

        var result = await harness.RunAsync(
            "notebooks",
            "repair",
            source,
            "--output",
            output);
        var check = await harness.RunAsync("notebooks", "check", output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("Notebook repair: completed"));
            Assert.That(result.StandardOutput, Does.Contain("Preflight: repairable"));
            Assert.That(result.StandardOutput, Does.Contain("Copied: metadata=6, pages=1"));
            Assert.That(result.StandardOutput,
                Does.Contain("Authoritative data copied: Metadata, Pages"));
            Assert.That(result.StandardOutput,
                Does.Contain("Derived data rebuilt: PageTags"));
            Assert.That(result.StandardOutput,
                Does.Match("Logical fingerprint: [0-9a-f]{64}:[0-9a-f]{64}"));
            Assert.That(result.StandardOutput, Does.Contain("Output validation: passed"));
            Assert.That(result.StandardOutput, Does.Contain("Source modified: no"));
            Assert.That(result.StandardOutput, Does.Contain("Output published: yes"));
            Assert.That(File.Exists(output), Is.True);
            Assert.That(check.ExitCode, Is.Zero, check.StandardError);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeSource));
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(beforeConfiguration));
        }
    }

    /// <summary>Verifies a completed refusal is a stdout report with validation exit.</summary>
    [Test]
    public async Task Repair_AuthoritativeValueDefect_ReportsRefusalOnStdout()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "invalid")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "invalid.mnote");
        var output = Path.Combine(harness.WorkingDirectory, "unused.mnote");
        await ExecuteAsync(
            source,
            "UPDATE Metadata SET Value = 'secret-invalid' WHERE Key = 'ReadOnly';");

        var result = await harness.RunAsync(
            "notebooks",
            "repair",
            source,
            "--output",
            output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("Notebook repair: refused"));
            Assert.That(result.StandardOutput, Does.Contain("Preflight: refused"));
            Assert.That(result.StandardOutput,
                Does.Contain("REFUSAL authoritative-value-invalid"));
            Assert.That(result.StandardOutput, Does.Contain("Output published: no"));
            Assert.That(result.StandardOutput, Does.Contain("CM2 archive"));
            Assert.That(result.StandardOutput, Does.Not.Contain("secret-invalid"));
            Assert.That(File.Exists(output), Is.False);
        }
    }

    /// <summary>Verifies an existing output is a fatal conflict and is never overwritten.</summary>
    [Test]
    public async Task Repair_ExistingOutput_ReturnsConflictOnStderr()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "source")).ExitCode, Is.Zero);
        var source = Path.Combine(harness.WorkingDirectory, "source.mnote");
        var output = Path.Combine(harness.WorkingDirectory, "existing.mnote");
        await File.WriteAllTextAsync(output, "keep me");

        var result = await harness.RunAsync(
            "notebooks",
            "repair",
            source,
            "--output",
            output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already exists"));
            Assert.That(result.StandardError, Does.Contain("different new --output"));
            Assert.That(await File.ReadAllTextAsync(output), Is.EqualTo("keep me"));
        }
    }

    /// <summary>Verifies a missing source retains the public not-found status.</summary>
    [Test]
    public async Task Repair_MissingSource_ReturnsNotFound()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync(
            "notebooks",
            "repair",
            "missing.mnote",
            "--output",
            "unused.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("does not exist"));
        }
    }

    private static async Task ExecuteAsync(string path, string commandText)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
