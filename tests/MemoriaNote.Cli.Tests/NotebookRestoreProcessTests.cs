using System.IO.Compression;
using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies archive v1 restore through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookRestoreProcessTests
{
    /// <summary>Verifies help documents file, standard-input, target, and dry-run modes.</summary>
    [Test]
    public async Task Help_DescribesArchiveTargetAndDryRunOptions()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("notebooks", "restore", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks restore"));
            Assert.That(
                result.StandardOutput,
                Does.Contain("mn notebooks restore [<archive>] --target <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--target <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--dry-run"));
            Assert.That(result.StandardOutput, Does.Contain("--workspace <directory>"));
            Assert.That(result.StandardOutput, Does.Contain("standard input"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies a file restore targets an explicit workspace without reading workspace state.
    /// </summary>
    [Test]
    public async Task FileRestore_ExplicitWorkspace_PreservesInputsAndAuthoritativeData()
    {
        using var harness = new CliProcessHarness();
        var sourcePath = await PrepareArchiveAsync(harness, "source", "source.zip", 2);
        var archivePath = Path.Combine(harness.WorkingDirectory, "source.zip");
        var destinationWorkspace = Path.Combine(harness.WorkingDirectory, "destination");
        Directory.CreateDirectory(destinationWorkspace);
        var configurationPath = Path.Combine(
            destinationWorkspace,
            WorkspaceConfigurationStore.FileName);
        const string malformedConfiguration = "format_version =";
        await File.WriteAllTextAsync(configurationPath, malformedConfiguration);
        var sourceBefore = await File.ReadAllBytesAsync(sourcePath);
        var archiveBefore = await File.ReadAllBytesAsync(archivePath);

        var result = await harness.RunAsync(
            "--workspace",
            destinationWorkspace,
            "notebooks",
            "restore",
            "source.zip",
            "--target",
            "restored");

        var destinationPath = Path.Combine(destinationWorkspace, "restored.mnote");
        var factory = CreateDatabaseFactory();
        var sourceMetadata = await new SqliteNotebookMetadataRepository(factory)
            .LoadAsync(sourcePath, CancellationToken.None);
        var restoredMetadata = await new SqliteNotebookMetadataRepository(factory)
            .LoadAsync(destinationPath, CancellationToken.None);
        var sourcePages = await new SqlitePageRepository(factory)
            .ListPageSummariesAsync(sourcePath, 0, 100, CancellationToken.None);
        var restoredPages = await new SqlitePageRepository(factory)
            .ListPageSummariesAsync(destinationPath, 0, 100, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Restore completed: "));
            Assert.That(
                result.StandardOutput,
                Does.Contain($"destination=\"{destinationPath}"));
            Assert.That(result.StandardOutput, Does.Match(@"metadata=\d+, pages=2\r?\n$"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(destinationPath), Is.True);
            Assert.That(restoredMetadata.Metadata.Name, Is.EqualTo(sourceMetadata.Metadata.Name));
            Assert.That(
                restoredPages.Select(page => page.PageId),
                Is.EqualTo(sourcePages.Select(page => page.PageId)));
            Assert.That(await File.ReadAllBytesAsync(sourcePath), Is.EqualTo(sourceBefore));
            Assert.That(await File.ReadAllBytesAsync(archivePath), Is.EqualTo(archiveBefore));
            Assert.That(
                await File.ReadAllTextAsync(configurationPath),
                Is.EqualTo(malformedConfiguration));
        }
    }

    /// <summary>Verifies omitted and explicit dash sources read exact archive bytes from stdin.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StandardInputRestore_Redirected_CreatesNotebook(bool explicitDash)
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "stream-source", "stream.zip", 1);
        var archiveBytes = await File.ReadAllBytesAsync(
            Path.Combine(harness.WorkingDirectory, "stream.zip"));
        var arguments = new List<string> { "notebooks", "restore" };
        if (explicitDash)
            arguments.Add("-");
        arguments.Add("--target");
        arguments.Add(explicitDash ? "explicit-stream" : "implicit-stream");

        var result = await harness.RunWithStandardInputAsync(
            arguments,
            archiveBytes,
            TimeSpan.FromSeconds(10));

        var destination = Path.Combine(
            harness.WorkingDirectory,
            (explicitDash ? "explicit-stream" : "implicit-stream") + ".mnote");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("pages=1"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(destination), Is.True);
        }
    }

    /// <summary>Verifies ./- is treated as a file path rather than standard input.</summary>
    [Test]
    public async Task FileRestore_ExplicitDashPath_ReadsFileNamedDash()
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "dash-source", "-", 0);

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "./-",
            "--target",
            "dash-restored");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("pages=0"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "dash-restored.mnote")),
                Is.True);
        }
    }

    /// <summary>Verifies dry-run validates file and stream inputs without publishing state.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task DryRun_ValidArchive_ReportsCountsWithoutCreatingNotebook(bool useStream)
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "dry-source", "dry.zip", 1);
        var arguments = new List<string> { "notebooks", "restore" };
        if (!useStream)
            arguments.Add("dry.zip");
        arguments.Add("--target");
        arguments.Add("dry-target");
        arguments.Add("--dry-run");

        var result = useStream
            ? await harness.RunWithStandardInputAsync(
                arguments,
                await File.ReadAllBytesAsync(
                    Path.Combine(harness.WorkingDirectory, "dry.zip")),
                TimeSpan.FromSeconds(10))
            : await harness.RunAsync(arguments.ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Restore validated: "));
            Assert.That(result.StandardOutput, Does.Contain("pages=1"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "dry-target.mnote")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

    /// <summary>Verifies an existing destination is preserved as a conflict.</summary>
    [Test]
    public async Task Restore_ExistingDestination_PreservesFileAndReturnsConflict()
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "conflict-source", "conflict.zip", 0);
        var destinationPath = Path.Combine(harness.WorkingDirectory, "existing.mnote");
        var existing = "keep existing notebook"u8.ToArray();
        await File.WriteAllBytesAsync(destinationPath, existing);

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "conflict.zip",
            "--target",
            "existing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already exists"));
            Assert.That(await File.ReadAllBytesAsync(destinationPath), Is.EqualTo(existing));
        }
    }

    /// <summary>Verifies invalid target forms are rejected without creating a notebook.</summary>
    [TestCase(null)]
    [TestCase("../outside")]
    [TestCase("nested/work")]
    [TestCase("upper.MNOTE")]
    [TestCase("work.db")]
    public async Task Restore_InvalidTarget_ReturnsValidationFailure(string? target)
    {
        using var harness = new CliProcessHarness();
        await PrepareArchiveAsync(harness, "target-source", "target.zip", 0);
        var arguments = new List<string> { "notebooks", "restore", "target.zip" };
        if (target != null)
        {
            arguments.Add("--target");
            arguments.Add(target);
        }

        var result = await harness.RunAsync(arguments.ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Is.Not.Empty);
            Assert.That(
                Directory.EnumerateFiles(harness.WorkingDirectory, "outside.mnote"),
                Is.Empty);
        }
    }

    /// <summary>Verifies a missing archive maps to not-found without destination output.</summary>
    [Test]
    public async Task FileRestore_MissingArchive_ReturnsNotFound()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "missing.zip",
            "--target",
            "restored");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("does not exist"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "restored.mnote")),
                Is.False);
        }
    }

    /// <summary>Verifies legacy candidates receive explicit manual-recovery guidance.</summary>
    [Test]
    public async Task FileRestore_LegacyArchive_ReturnsMigrationGuidance()
    {
        using var harness = new CliProcessHarness();
        var archivePath = Path.Combine(harness.WorkingDirectory, "legacy.zip");
        using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var metadata = archive.CreateEntry("metadata.json");
            await using var content = metadata.Open();
            await content.WriteAsync("{}"u8.ToArray());
        }

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "legacy.zip",
            "--target",
            "restored");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("Legacy archives"));
            Assert.That(result.StandardError, Does.Contain("older Memoria Note CLI"));
            Assert.That(result.StandardError, Does.Contain("NCLI-660"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "restored.mnote")),
                Is.False);
        }
    }

    static async Task<string> PrepareArchiveAsync(
        CliProcessHarness harness,
        string notebookName,
        string archiveName,
        int pageCount)
    {
        var create = await harness.RunAsync("create", notebookName);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var notebookPath = Path.Combine(
            harness.WorkingDirectory,
            notebookName + ".mnote");
        var repository = new SqlitePageRepository(CreateDatabaseFactory());
        for (var index = 1; index <= pageCount; index++)
        {
            await repository.CreatePageAsync(
                notebookPath,
                "Page " + index,
                "Body " + index,
                null,
                CancellationToken.None);
        }

        var backup = await harness.RunAsync(
            "notebooks",
            "backup",
            "--notebook",
            notebookName,
            archiveName == "-" ? "./-" : archiveName);
        Assert.That(backup.ExitCode, Is.Zero, backup.StandardError);
        return notebookPath;
    }

    static SqliteNotebookDbContextFactory CreateDatabaseFactory()
    {
        return new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
    }
}
