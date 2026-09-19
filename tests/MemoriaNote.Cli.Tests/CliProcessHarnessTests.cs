using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies the isolated out-of-process CLI test harness.
/// </summary>
[TestFixture]
public sealed class CliProcessHarnessTests
{
    /// <summary>
    /// Verifies that root help describes the public commands on standard output.
    /// </summary>
    [Test]
    public async Task Help_DescribesPublicCommandsOnStandardOutput()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                result.StandardOutput,
                Does.Contain(
                    "A lightweight, cross-platform CLI for creating, organizing, and editing workspace-based notes"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Terminal.Gui"));
            Assert.That(result.StandardOutput, Does.Contain("Temporarily unavailable"));
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }

        var publicCommands = new[]
        {
            "config",
            "create",
            "edit",
            "export",
            "find",
            "import",
            "list",
            "new",
            "work"
        };
        foreach (var command in publicCommands)
        {
            Assert.That(
                ContainsCommand(result.StandardOutput, command),
                Is.True,
                $"Root help did not describe the '{command}' command.");
        }
    }

    /// <summary>
    /// Verifies that create writes a current live notebook relative to the process directory.
    /// </summary>
    [Test]
    public async Task Create_InCurrentDirectory_CreatesVerifiedNotebookWithoutConfiguration()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");

        var result = await harness.RunAsync("create", "work.mnote");

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
            Assert.That(result.StandardOutput, Does.Contain("<notebook-file>"));
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
            Assert.That(result.StandardError, Does.Contain("notebook file is required"));
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
            Assert.That(result.StandardError, Does.Contain("inside the workspace"));
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

    /// <summary>
    /// Verifies that find reports its temporary pause without initializing configuration or storage.
    /// </summary>
    [Test]
    public async Task FindWithoutQuery_IsPausedWithoutInitializingApplication()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("find");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(
                result.StandardError,
                Is.EqualTo(
                    "Error: The find command is temporarily unavailable. Use 'mn list [name]' to list page names; " +
                    "full-text search will be redesigned after the initial release." +
                    Environment.NewLine));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies that find has the same paused contract when a query is supplied.</summary>
    [Test]
    public async Task FindWithQuery_HasTheSamePausedContract()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("find", "Roadmap");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(
                result.StandardError,
                Is.EqualTo(
                    "Error: The find command is temporarily unavailable. Use 'mn list [name]' to list page names; " +
                    "full-text search will be redesigned after the initial release." +
                    Environment.NewLine));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies that find help explains its pause and the limited list alternative.</summary>
    [Test]
    public async Task FindHelp_ExplainsPauseAndScope()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("find", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn find"));
            Assert.That(result.StandardOutput, Does.Contain("<query>"));
            Assert.That(result.StandardOutput, Does.Contain("Temporarily unavailable"));
            Assert.That(result.StandardOutput, Does.Contain("mn list [name]"));
            Assert.That(result.StandardOutput, Does.Contain("not full-text search"));
            Assert.That(result.StandardOutput, Does.Contain("separate follow-up"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
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
    /// Verifies that a missing import directory uses the not-found exit code.
    /// </summary>
    [Test]
    public async Task ImportMissingDirectory_ReturnsNotFound()
    {
        using var harness = new CliProcessHarness();
        var missingDirectory = Path.Combine(
            harness.WorkingDirectory,
            "missing-import-directory");

        var result = await harness.RunAsync("import", missingDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(
                result.StandardError,
                Is.EqualTo("Error: No such directory" + Environment.NewLine));
        }
    }

    /// <summary>
    /// Verifies that usage help accompanies a missing import directory.
    /// </summary>
    [Test]
    public async Task ImportWithoutDirectory_WritesHelpAndReturnsFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("import");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn import"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>
    /// Verifies that a list handler failure reaches the process exit code.
    /// </summary>
    [Test]
    public async Task ListHandlerFailure_ReturnsFailure()
    {
        using var harness = new CliProcessHarness();
        await File.WriteAllTextAsync(
            harness.ApplicationDataDirectory,
            "This file prevents creation of the application data directory.");

        var result = await harness.RunAsync("list");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(5));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Error: "));
        }
    }

    /// <summary>
    /// Verifies that CLI configuration is written beneath isolated application data.
    /// </summary>
    [Test]
    public async Task ConfigShow_WritesConfigurationToIsolatedApplicationData()
    {
        using var harness = new CliProcessHarness();

        await harness.RunAsync("config", "show");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(harness.ConfigurationPath), Is.True);
            Assert.That(
                Path.GetFullPath(harness.ConfigurationPath),
                Does.StartWith(Path.GetFullPath(harness.ApplicationDataRoot)));
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that invalid configuration is quarantined and reported to the user.
    /// </summary>
    [Test]
    public async Task ConfigShow_InvalidConfiguration_RecoversWithWarning()
    {
        using var harness = new CliProcessHarness();
        const string invalidConfiguration = "{ invalid configuration";
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        await File.WriteAllTextAsync(
            harness.ConfigurationPath,
            invalidConfiguration);

        var result = await harness.RunAsync("config", "show");

        var recoveryFiles = Directory.GetFiles(
            harness.ApplicationDataDirectory,
            "configuration.json.corrupt-*");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Does.StartWith("Warning: "));
            Assert.That(
                result.StandardError,
                Does.Contain("Default configuration has been created."));
            Assert.That(recoveryFiles, Has.Length.EqualTo(1));
            Assert.That(
                await File.ReadAllTextAsync(recoveryFiles[0]),
                Is.EqualTo(invalidConfiguration));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.True);
            Assert.That(result.StandardOutput, Does.Contain("\"Workgroup\""));
        }
    }

    static bool ContainsCommand(string help, string command)
    {
        return help.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line => line.StartsWith(command + " ", StringComparison.Ordinal));
    }
}
