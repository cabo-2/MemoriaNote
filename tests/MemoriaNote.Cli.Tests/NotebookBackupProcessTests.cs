using System.Text;
using MemoriaNote.Archive;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies archive v1 backup through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookBackupProcessTests
{
    /// <summary>Verifies help documents file and standard-output backup modes.</summary>
    [Test]
    public async Task Help_DescribesArchiveAndNotebookOptions()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("notebooks", "backup", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks backup"));
            Assert.That(
                result.StandardOutput,
                Does.Contain("mn notebooks backup [<archive>]"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--workspace <directory>"));
            Assert.That(result.StandardOutput, Does.Contain("standard output"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies a selected notebook is backed up to a new file with a one-line summary.
    /// </summary>
    [Test]
    public async Task FileBackup_SelectedNotebook_CreatesValidatedArchiveAndSummary()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "selected");
        await AddPagesAsync(notebookPath, 1);
        var configurationPath = WorkspaceConfigurationPath(harness);
        var configurationBefore = await File.ReadAllBytesAsync(configurationPath);
        var notebookBefore = await File.ReadAllBytesAsync(notebookPath);
        var archivePath = Path.Combine(harness.WorkingDirectory, "selected.backup.zip");

        var result = await harness.RunAsync(
            "notebooks",
            "backup",
            "selected.backup.zip");

        var report = await ValidateFileAsync(archivePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Backup completed: "));
            Assert.That(result.StandardOutput, Does.Contain($"source=\"{notebookPath}"));
            Assert.That(result.StandardOutput, Does.Contain($"archive=\"{archivePath}"));
            Assert.That(result.StandardOutput, Does.Match(@"metadata=\d+, pages=1\r?\n$"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(report.IsValid, Is.True);
            Assert.That(
                await File.ReadAllBytesAsync(configurationPath),
                Is.EqualTo(configurationBefore));
            Assert.That(await File.ReadAllBytesAsync(notebookPath), Is.EqualTo(notebookBefore));
        }
    }

    /// <summary>
    /// Verifies an explicit notebook is resolved without reading or changing current selection.
    /// </summary>
    [Test]
    public async Task FileBackup_ExplicitNotebook_IgnoresMalformedWorkspaceConfiguration()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateNotebookAsync(harness, "explicit");
        await AddPagesAsync(notebookPath, 2);
        var configurationPath = WorkspaceConfigurationPath(harness);
        const string malformedConfiguration = "format_version =";
        await File.WriteAllTextAsync(configurationPath, malformedConfiguration);

        var result = await harness.RunAsync(
            "notebooks",
            "backup",
            "--notebook",
            "explicit",
            "explicit.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("pages=2"));
            Assert.That(
                await File.ReadAllTextAsync(configurationPath),
                Is.EqualTo(malformedConfiguration));
            Assert.That(
                (await ValidateFileAsync(
                    Path.Combine(harness.WorkingDirectory, "explicit.zip"))).IsValid,
                Is.True);
        }
    }

    /// <summary>Verifies omitted and explicit dash destinations emit only archive bytes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StandardOutputBackup_Redirected_EmitsValidatedArchiveOnly(
        bool explicitDash)
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "stream");
        await AddPagesAsync(notebookPath, 1);
        var arguments = new List<string> { "notebooks", "backup" };
        if (explicitDash)
            arguments.Add("-");

        var result = await harness.RunAsync(
            arguments,
            null,
            TimeSpan.FromSeconds(10));

        using var archive = new MemoryStream(result.StandardOutputBytes, writable: false);
        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutputBytes, Is.Not.Empty);
            Assert.That(report.IsValid, Is.True);
            Assert.That(
                Encoding.UTF8.GetString(result.StandardOutputBytes),
                Does.Not.Contain("Backup completed"));
        }
    }

    /// <summary>Verifies a path spelling such as ./- creates a file named dash.</summary>
    [Test]
    public async Task FileBackup_ExplicitDashPath_CreatesFileInsteadOfUsingStandardOutput()
    {
        using var harness = new CliProcessHarness();
        await CreateAndSelectNotebookAsync(harness, "dash");

        var result = await harness.RunAsync("notebooks", "backup", "./-");

        var archivePath = Path.Combine(harness.WorkingDirectory, "-");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Backup completed: "));
            Assert.That(File.Exists(archivePath), Is.True);
            Assert.That((await ValidateFileAsync(archivePath)).IsValid, Is.True);
        }
    }

    /// <summary>Verifies an existing destination is preserved and reported as a conflict.</summary>
    [Test]
    public async Task FileBackup_ExistingDestination_PreservesFileAndReturnsConflict()
    {
        using var harness = new CliProcessHarness();
        await CreateAndSelectNotebookAsync(harness, "conflict");
        var archivePath = Path.Combine(harness.WorkingDirectory, "existing.zip");
        var existing = "keep existing archive"u8.ToArray();
        await File.WriteAllBytesAsync(archivePath, existing);

        var result = await harness.RunAsync(
            "notebooks",
            "backup",
            "existing.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already exists"));
            Assert.That(await File.ReadAllBytesAsync(archivePath), Is.EqualTo(existing));
        }
    }

    /// <summary>Verifies an empty file destination is rejected without creating output.</summary>
    [Test]
    public async Task FileBackup_WhitespaceDestination_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();
        await CreateAndSelectNotebookAsync(harness, "invalid-path");

        var result = await harness.RunAsync("notebooks", "backup", " ");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("path cannot be empty"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, " ")),
                Is.False);
        }
    }

    /// <summary>Verifies a metadata read-only notebook remains eligible for backup.</summary>
    [Test]
    public async Task FileBackup_ReadOnlyNotebook_SucceedsWithoutChangingSource()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "read-only");
        var factory = CreateDatabaseFactory();
        await new SqliteNotebookMetadataRepository(factory).UpdateAsync(
            notebookPath,
            new NotebookMetadataPatch().SetReadOnly(true),
            CancellationToken.None);
        var notebookBefore = await File.ReadAllBytesAsync(notebookPath);

        var result = await harness.RunAsync(
            "notebooks",
            "backup",
            "read-only.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                (await ValidateFileAsync(
                    Path.Combine(harness.WorkingDirectory, "read-only.zip"))).IsValid,
                Is.True);
            Assert.That(await File.ReadAllBytesAsync(notebookPath), Is.EqualTo(notebookBefore));
        }
    }

    /// <summary>Verifies stdout mode still requires an explicitly selected notebook.</summary>
    [Test]
    public async Task StandardOutputBackup_WithoutSelection_ReturnsConflictWithoutBytes()
    {
        using var harness = new CliProcessHarness();
        await CreateNotebookAsync(harness, "available");

        var result = await harness.RunAsync("notebooks", "backup");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutputBytes, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
        }
    }

    static async Task<string> CreateAndSelectNotebookAsync(
        CliProcessHarness harness,
        string name)
    {
        var notebookPath = await CreateNotebookAsync(harness, name);
        var use = await harness.RunAsync("use", name);
        Assert.That(use.ExitCode, Is.Zero, use.StandardError);
        return notebookPath;
    }

    static async Task<string> CreateNotebookAsync(CliProcessHarness harness, string name)
    {
        var create = await harness.RunAsync("create", name);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        return Path.Combine(harness.WorkingDirectory, name + ".mnote");
    }

    static async Task AddPagesAsync(string notebookPath, int count)
    {
        var repository = new SqlitePageRepository(CreateDatabaseFactory());
        for (var index = 1; index <= count; index++)
        {
            await repository.CreatePageAsync(
                notebookPath,
                "Page " + index,
                "Body " + index,
                null,
                CancellationToken.None);
        }
    }

    static async Task<ArchiveV1ValidationReport> ValidateFileAsync(string archivePath)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return await new ArchiveV1Reader().ValidateAsync(stream, CancellationToken.None);
    }

    static SqliteNotebookDbContextFactory CreateDatabaseFactory()
    {
        return new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
    }

    static string WorkspaceConfigurationPath(CliProcessHarness harness)
    {
        return Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
    }
}
