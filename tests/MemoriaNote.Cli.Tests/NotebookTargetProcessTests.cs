using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook target resolution through the CLI process boundary.</summary>
[TestFixture]
public sealed class NotebookTargetProcessTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>Verifies new help exposes the explicit notebook option.</summary>
    [Test]
    public async Task NewHelp_DescribesNotebookOption()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("new", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn new"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--workspace <directory>"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies an unselected workspace is not resolved implicitly.</summary>
    [Test]
    public async Task New_WithoutSelection_ReturnsConflict()
    {
        using var harness = CreateEditorHarness();

        var result = await harness.RunAsync("new", "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies even one root notebook is not selected automatically.</summary>
    [Test]
    public async Task New_WithOneUnselectedNotebook_ReturnsConflict()
    {
        using var harness = CreateEditorHarness();
        await CreateNotebookAsync(harness, "only.mnote");

        var result = await harness.RunAsync("new", "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
        }
    }

    /// <summary>Verifies an explicit notebook selects one target from multiple candidates.</summary>
    [Test]
    public async Task New_WithExplicitNotebook_CreatesPageOnlyInThatNotebook()
    {
        using var harness = CreateEditorHarness();
        var firstPath = await CreateNotebookAsync(harness, "first.mnote");
        var secondPath = await CreateNotebookAsync(harness, "second.mnote");

        var result = await harness.RunAsync(
            "new",
            "--notebook",
            "second",
            "Page");

        var repository = CreatePageRepository();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                await repository.ListPagesByHeadingAsync(
                    firstPath,
                    "Page",
                    CancellationToken.None),
                Is.Empty);
            Assert.That(
                await repository.ListPagesByHeadingAsync(
                    secondPath,
                    "Page",
                    CancellationToken.None),
                Has.Count.EqualTo(1));
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

    /// <summary>Verifies an explicitly named subdirectory notebook is rejected.</summary>
    [Test]
    public async Task New_WithExplicitSubdirectoryNotebook_ReturnsValidationFailure()
    {
        using var harness = CreateEditorHarness();
        var directoryPath = Path.Combine(harness.WorkingDirectory, "nested");
        Directory.CreateDirectory(directoryPath);
        var createResult = await harness.RunAsync(
            "--workspace",
            directoryPath,
            "create",
            "work.mnote");
        Assert.That(createResult.ExitCode, Is.Zero, createResult.StandardError);

        var explicitResult = await harness.RunAsync(
            "new",
            "--notebook",
            "nested/work.mnote",
            "Explicit");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                explicitResult.ExitCode,
                Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(explicitResult.StandardOutput, Is.Empty);
            Assert.That(explicitResult.StandardError, Does.Contain("path separators"));
        }
    }

    /// <summary>Verifies an explicit missing notebook does not fall back to another file.</summary>
    [Test]
    public async Task New_WithMissingExplicitNotebook_DoesNotFallback()
    {
        using var harness = CreateEditorHarness();
        await CreateNotebookAsync(harness, "available.mnote");

        var result = await harness.RunAsync(
            "new",
            "--notebook",
            "missing.mnote",
            "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("missing.mnote"));
        }
    }

    /// <summary>Verifies a missing saved selection does not fall back to an available notebook.</summary>
    [Test]
    public async Task New_WithMissingSelectedNotebook_DoesNotFallback()
    {
        using var harness = CreateEditorHarness();
        await CreateNotebookAsync(harness, "available.mnote");
        await File.WriteAllTextAsync(
            Path.Combine(
                harness.WorkingDirectory,
                WorkspaceConfigurationStore.FileName),
            "format_version = 1\ncurrent_notebook = \"missing.mnote\"\n");

        var result = await harness.RunAsync("new", "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("missing.mnote"));
        }
    }

    /// <summary>Verifies an invocation override neither reads nor changes workspace selection.</summary>
    [Test]
    public async Task New_WithExplicitNotebook_IgnoresMalformedConfiguration()
    {
        using var harness = CreateEditorHarness();
        var notebookPath = await CreateNotebookAsync(harness, "work.mnote");
        var configurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        const string malformedConfiguration = "format_version =";
        await File.WriteAllTextAsync(configurationPath, malformedConfiguration);

        var result = await harness.RunAsync(
            "new",
            "--notebook",
            "work",
            "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(
                await File.ReadAllTextAsync(configurationPath),
                Is.EqualTo(malformedConfiguration));
            Assert.That(
                await CreatePageRepository().ListPagesByHeadingAsync(
                    notebookPath,
                    "Page",
                    CancellationToken.None),
                Has.Count.EqualTo(1));
        }
    }

    /// <summary>Verifies an invalid saved notebook is reported without fallback.</summary>
    [Test]
    public async Task New_WithInvalidSelectedNotebook_ReturnsValidationFailure()
    {
        using var harness = CreateEditorHarness();
        var notebookPath = Path.Combine(harness.WorkingDirectory, "invalid.mnote");
        const string invalidContent = "not a SQLite notebook";
        await File.WriteAllTextAsync(notebookPath, invalidContent);
        await File.WriteAllTextAsync(
            Path.Combine(
                harness.WorkingDirectory,
                WorkspaceConfigurationStore.FileName),
            "format_version = 1\ncurrent_notebook = \"invalid.mnote\"\n");

        var result = await harness.RunAsync("new", "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("not a current Memoria Note notebook"));
            Assert.That(
                await File.ReadAllTextAsync(notebookPath),
                Is.EqualTo(invalidContent));
        }
    }

    /// <summary>Verifies explicit notebook paths require the live-notebook extension.</summary>
    [Test]
    public async Task New_WithInvalidNotebookExtension_ReturnsValidationFailure()
    {
        using var harness = CreateEditorHarness();

        var result = await harness.RunAsync(
            "new",
            "--notebook",
            "work.db",
            "Page");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain(".mnote"));
        }
    }

    /// <summary>Verifies the explicit workspace is also the notebook resolution base.</summary>
    [Test]
    public async Task New_WithExplicitWorkspace_UsesThatWorkspace()
    {
        using var harness = CreateEditorHarness();
        var workspacePath = Path.Combine(harness.WorkingDirectory, "notes");
        Directory.CreateDirectory(workspacePath);
        var createResult = await harness.RunAsync(
            "--workspace",
            "notes",
            "create",
            "work.mnote");
        Assert.That(createResult.ExitCode, Is.Zero);
        var useResult = await harness.RunAsync(
            "--workspace",
            "notes",
            "use",
            "work");
        Assert.That(useResult.ExitCode, Is.Zero, useResult.StandardError);

        var result = await harness.RunAsync(
            "--workspace",
            "notes",
            "new",
            "Page");

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(
            await CreatePageRepository().ListPagesByHeadingAsync(
                Path.Combine(workspacePath, "work.mnote"),
                "Page",
                CancellationToken.None),
            Has.Count.EqualTo(1));
    }

    static CliProcessHarness CreateEditorHarness()
    {
        var harness = new CliProcessHarness();
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Body");
        return harness;
    }

    static async Task<string> CreateNotebookAsync(
        CliProcessHarness harness,
        string relativePath)
    {
        var result = await harness.RunAsync("create", relativePath);
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        return Path.GetFullPath(relativePath, harness.WorkingDirectory);
    }

    static SqlitePageRepository CreatePageRepository()
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        return new SqlitePageRepository(factory);
    }
}
