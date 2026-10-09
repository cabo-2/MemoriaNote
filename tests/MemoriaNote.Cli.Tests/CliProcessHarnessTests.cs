using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies the isolated out-of-process CLI test harness.
/// </summary>
[TestFixture]
[Category("Process")]
public sealed class CliProcessHarnessTests
{
    /// <summary>
    /// Verifies that create writes a current live notebook relative to the process directory.
    /// </summary>
    [Test]
    public async Task Create_InCurrentDirectory_CreatesVerifiedNotebookWithoutConfiguration()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");

        var result = await harness.RunAsync("create", "work");

        var factory = new SqliteNotebookDbContextFactory(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var metadataRepository = new SqliteNotebookMetadataRepository(factory);
        var metadata = await metadataRepository.LoadAsync(
            notebookPath,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain(notebookPath));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(notebookPath), Is.True);
            Assert.That(metadata.HasIssues, Is.False);
            Assert.That(metadata.Metadata.Name, Is.EqualTo("work"));
            Assert.That(metadata.Metadata.Title, Is.EqualTo("work"));
            Assert.That(
                metadata.Metadata.Version,
                Is.EqualTo(NotebookDbContext.CurrentVersion));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    "mn-workspace.toml")),
                Is.False);
        }
    }

    /// <summary>
    /// Verifies that the global workspace option changes the base of a relative notebook path.
    /// </summary>
    [Test]
    public async Task Create_WithWorkspace_CreatesRelativeToExplicitWorkspace()
    {
        using var harness = new CliProcessHarness();
        var workspacePath = Path.Combine(harness.WorkingDirectory, "notes");
        Directory.CreateDirectory(workspacePath);

        var result = await harness.RunAsync(
            "--workspace",
            "notes",
            "create",
            "work.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                File.Exists(Path.Combine(workspacePath, "work.mnote")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "work.mnote")),
                Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies that create help documents its required file and global workspace.</summary>
    [Test]
    public async Task CreateHelp_DescribesNotebookAndWorkspaceArguments()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("create", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn create"));
            Assert.That(result.StandardOutput, Does.Contain("<notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--workspace <directory>"));
            Assert.That(result.StandardOutput, Does.Contain(".mnote"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies that create reports its required notebook argument as invalid input.</summary>
    [Test]
    public async Task Create_WithoutNotebookFile_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("create");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("notebook is required"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies that create never replaces an existing output file.</summary>
    [Test]
    public async Task Create_WhenFileExists_PreservesFileAndReturnsConflict()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        const string existingContent = "not a notebook";
        await File.WriteAllTextAsync(notebookPath, existingContent);

        var result = await harness.RunAsync("create", "work.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(4));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error: "));
            Assert.That(
                await File.ReadAllTextAsync(notebookPath),
                Is.EqualTo(existingContent));
        }
    }

    /// <summary>Verifies that create rejects a non-live-notebook extension.</summary>
    [Test]
    public async Task Create_WithInvalidExtension_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("create", "work.db");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain(".mnote"));
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    /// <summary>Verifies that create cannot escape the selected workspace.</summary>
    [Test]
    public async Task Create_OutsideWorkspace_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();
        var workspacePath = Path.Combine(harness.WorkingDirectory, "notes");
        Directory.CreateDirectory(workspacePath);

        var result = await harness.RunAsync(
            "--workspace",
            workspacePath,
            "create",
            "../outside.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("without path separators"));
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "outside.mnote")),
                Is.False);
        }
    }

    /// <summary>Verifies that create reports a missing workspace as not found.</summary>
    [Test]
    public async Task Create_WithMissingWorkspace_ReturnsNotFound()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync(
            "--workspace",
            "missing",
            "create",
            "work.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("does not exist"));
        }
    }

    /// <summary>Verifies that create maps an unavailable output path to storage failure.</summary>
    [Test]
    public async Task Create_WhenOutputPathIsDirectory_ReturnsStorageFailure()
    {
        using var harness = new CliProcessHarness();
        var outputPath = Path.Combine(harness.WorkingDirectory, "blocked.mnote");
        Directory.CreateDirectory(outputPath);

        var result = await harness.RunAsync("create", "blocked.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(5));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error: "));
            Assert.That(Directory.Exists(outputPath), Is.True);
        }
    }

    /// <summary>Verifies removed legacy commands are unknown and have no configuration side effects.</summary>
    [TestCase("work")]
    [TestCase("import")]
    [TestCase("export")]
    [TestCase("list")]
    [TestCase("find")]
    public async Task RemovedLegacyCommand_IsUnrecognizedWithoutCreatingConfiguration(
        string command)
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync(command);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(
                result.StandardError,
                Does.Contain($"Unrecognized command or argument '{command}'"));
            Assert.That(result.StandardOutput, Does.Contain("--help"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that parser errors use standard error and return failure.
    /// </summary>
    [Test]
    public async Task UnknownCommand_WritesDiagnosticAndReturnsFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("unknown-command");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(
                result.StandardError,
                Does.Contain("Unrecognized command or argument 'unknown-command'"));
            Assert.That(result.StandardOutput, Does.Contain("--help"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>
    /// Verifies that command argument validation uses standard error and returns failure.
    /// </summary>
    [Test]
    public async Task NewWithoutName_WritesDiagnosticAndReturnsFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("new");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("Error: No name"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>
    /// Verifies that a missing notebook import directory uses the not-found exit code.
    /// </summary>
    [Test]
    public async Task NotebookImportMissingDirectory_ReturnsNotFound()
    {
        using var harness = new CliProcessHarness();
        var missingDirectory = Path.Combine(
            harness.WorkingDirectory,
            "missing-import-directory");

        var result = await harness.RunAsync(
            "notebooks",
            "import",
            missingDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(
                result.StandardError,
                Does.Contain("import directory does not exist"));
        }
    }

    /// <summary>
    /// Verifies that usage help accompanies a missing notebook import directory.
    /// </summary>
    [Test]
    public async Task NotebookImportWithoutDirectory_WritesHelpAndReturnsFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("notebooks", "import");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks import"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>
    /// Verifies ls reports a missing selection without creating configuration.
    /// </summary>
    [Test]
    public async Task Ls_WithoutSelection_ReturnsConflict()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("ls");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

}
