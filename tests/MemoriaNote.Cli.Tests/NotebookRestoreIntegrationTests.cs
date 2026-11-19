using System.IO.Compression;
using System.Text;
using MemoriaNote.Archive;
using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies archive v1 restore through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class NotebookRestoreIntegrationTests
{
    /// <summary>Verifies help documents file, standard-input, target, and dry-run modes.</summary>
    [Test]
    public async Task Help_DescribesArchiveTargetAndDryRunOptions()
    {
        using var harness = new CliInProcessHarness();

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
            Assert.That(result.StandardOutput, Does.Contain("archive v1/CM2"));
            Assert.That(result.StandardOutput, Does.Contain("content model 1"));
            Assert.That(result.StandardOutput, Does.Contain("mn notebooks migrate"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies a file restore targets an explicit workspace without reading workspace state.
    /// </summary>
    [Test]
    public async Task FileRestore_ExplicitWorkspace_PreservesInputsAndAuthoritativeData()
    {
        using var harness = new CliInProcessHarness();
        var sourcePath = await PrepareArchiveAsync(harness, "source", "source.zip", 2);
        var archivePath = Path.Combine(harness.WorkingDirectory, "source.zip");
        var destinationWorkspace = Path.Combine(harness.WorkingDirectory, "destination");
        Directory.CreateDirectory(destinationWorkspace);
        var configurationPath = Path.Combine(
            destinationWorkspace,
            WorkspaceConfigurationStore.FileName);
        const string malformedConfiguration = "format_version =";
        await File.WriteAllTextAsync(configurationPath, malformedConfiguration);
        var sourceBefore = await harness.ReadDatabaseBytesAsync(sourcePath);
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
        var sourceMetadata = await SchemaV2TestNotebook.GetMetadataAsync(sourcePath);
        var restoredMetadata = await SchemaV2TestNotebook.GetMetadataAsync(destinationPath);
        var sourcePages = await new SchemaV2TestPageRepository()
            .ListPageSummariesAsync(sourcePath, 0, 100, CancellationToken.None);
        var restoredPages = await new SchemaV2TestPageRepository()
            .ListPageSummariesAsync(destinationPath, 0, 100, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Restore completed: "));
            Assert.That(
                result.StandardOutput,
                Does.Contain($"destination=\"{destinationPath}"));
            Assert.That(result.StandardOutput, Does.Match(@"metadata=\d+, pages=2\.\r?\n"));
            Assert.That(result.StandardOutput, Does.Contain(
                "Workspace selection was not changed"));
            Assert.That(result.StandardOutput, Does.Contain(
                $"mn --workspace \"{destinationWorkspace}\" use \"restored.mnote\""));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(destinationPath), Is.True);
            Assert.That(restoredMetadata.Values, Is.EqualTo(sourceMetadata.Values));
            Assert.That(restoredMetadata.Values.ContainsKey("Name"), Is.False);
            Assert.That(restoredMetadata.Values.ContainsKey("Version"), Is.False);
            Assert.That(
                restoredPages.Select(page => page.PageId),
                Is.EqualTo(sourcePages.Select(page => page.PageId)));
            Assert.That(
                await harness.ReadDatabaseBytesAsync(sourcePath),
                Is.EqualTo(sourceBefore));
            Assert.That(await File.ReadAllBytesAsync(archivePath), Is.EqualTo(archiveBefore));
            Assert.That(
                await File.ReadAllTextAsync(configurationPath),
                Is.EqualTo(malformedConfiguration));
        }
    }

    /// <summary>Verifies ./- is treated as a file path rather than standard input.</summary>
    [Test]
    public async Task FileRestore_ExplicitDashPath_ReadsFileNamedDash()
    {
        using var harness = new CliInProcessHarness();
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

    /// <summary>Verifies dry-run validates a file without publishing state.</summary>
    [Test]
    public async Task DryRun_ValidArchive_ReportsCountsWithoutCreatingNotebook()
    {
        using var harness = new CliInProcessHarness();
        await PrepareArchiveAsync(harness, "dry-source", "dry.zip", 1);

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "dry.zip",
            "--target",
            "dry-target",
            "--dry-run");

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
        using var harness = new CliInProcessHarness();
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
        using var harness = new CliInProcessHarness();
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
        using var harness = new CliInProcessHarness();

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

    /// <summary>Verifies pre-v1 candidates receive explicit manual-recovery guidance.</summary>
    [Test]
    public async Task FileRestore_PreV1Candidate_ReturnsManualRecoveryGuidance()
    {
        using var harness = new CliInProcessHarness();
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
            Assert.That(result.StandardError, Does.Contain("manual recovery"));
            Assert.That(result.StandardError, Does.Not.Contain("NCLI-"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "restored.mnote")),
                Is.False);
        }
    }

    /// <summary>Verifies exact CM1 inputs are routed to the migration command.</summary>
    [Test]
    public async Task FileRestore_ContentModel1_ReturnsMigrationCommandGuidance()
    {
        using var harness = new CliInProcessHarness();
        var archivePath = Path.Combine(harness.WorkingDirectory, "cm1.mnarchive");
        await CreateCm1ArchiveAsync(archivePath);

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "cm1.mnarchive",
            "--target",
            "restored");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("content model 1 (CM1)"));
            Assert.That(result.StandardError, Does.Contain("mn notebooks migrate"));
            Assert.That(result.StandardError, Does.Contain("--output"));
            Assert.That(result.StandardError, Does.Not.Contain("older Memoria Note CLI"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "restored.mnote")),
                Is.False);
        }
    }

    /// <summary>Verifies unknown tuples are not misrouted to legacy migration.</summary>
    [Test]
    public async Task FileRestore_UnknownTuple_DoesNotSuggestMigration()
    {
        using var harness = new CliInProcessHarness();
        var archivePath = Path.Combine(harness.WorkingDirectory, "future.mnarchive");
        await CreateCm1ArchiveAsync(archivePath);
        await RewriteManifestAsync(
            archivePath,
            text => text.Replace(
                "\"contentModelVersion\": 1",
                "\"contentModelVersion\": 7",
                StringComparison.Ordinal));

        var result = await harness.RunAsync(
            "notebooks",
            "restore",
            "future.mnarchive",
            "--target",
            "restored");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain(
                "not a valid supported archive"));
            Assert.That(result.StandardError, Does.Not.Contain("notebooks migrate"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "restored.mnote")),
                Is.False);
        }
    }

    static async Task<string> PrepareArchiveAsync(
        CliInProcessHarness harness,
        string notebookName,
        string archiveName,
        int pageCount)
    {
        var create = await harness.RunAsync("create", notebookName);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var notebookPath = Path.Combine(
            harness.WorkingDirectory,
            notebookName + ".mnote");
        var repository = new SchemaV2TestPageRepository();
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

    static async Task CreateCm1ArchiveAsync(string path)
    {
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote.Tests", "ecli-1180-unit-5"),
            new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ArchiveV1MetadataRow("Name", "cm1"),
                new ArchiveV1MetadataRow("Title", "CM1"),
                new ArchiveV1MetadataRow("Version", "1")
            },
            EmptyCm1Pages());
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await new ArchiveV1Writer().WriteAsync(
            output,
            request,
            CancellationToken.None);
    }

    static async IAsyncEnumerable<ArchiveV1PageRow> EmptyCm1Pages()
    {
        await Task.CompletedTask;
        yield break;
    }

    static async Task RewriteManifestAsync(
        string archivePath,
        Func<string, string> rewrite)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var manifest = archive.GetEntry("manifest.json") ??
            throw new InvalidDataException("The archive manifest is missing.");
        string text;
        using (var reader = new StreamReader(
            manifest.Open(),
            new UTF8Encoding(false, true)))
        {
            text = await reader.ReadToEndAsync();
        }
        manifest.Delete();
        var replacement = archive.CreateEntry("manifest.json");
        await using var stream = replacement.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(rewrite(text));
    }

}
