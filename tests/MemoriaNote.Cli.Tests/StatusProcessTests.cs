using MemoriaNote.Cli.Tests.Infrastructure;
using MemoriaNote.Models;
using MemoriaNote.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies workspace status diagnostics through the CLI boundary.</summary>
[TestFixture]
public sealed class StatusProcessTests
{
    /// <summary>Verifies status is discoverable as a top-level command.</summary>
    [Test]
    public async Task Help_DescribesWorkspaceStatus()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("status", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn status"));
            Assert.That(result.StandardOutput, Does.Contain("workspace location"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies an unconfigured workspace is root and the parameterless command is identical.
    /// </summary>
    [Test]
    public async Task Execute_UnconfiguredRoot_MatchesParameterlessCommandWithoutChanges()
    {
        using var harness = new CliProcessHarness();

        var status = await harness.RunAsync("status");
        var parameterless = await harness.RunAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ExitCode, Is.Zero, status.StandardError);
            Assert.That(parameterless.ExitCode, Is.EqualTo(status.ExitCode));
            Assert.That(parameterless.StandardOutput, Is.EqualTo(status.StandardOutput));
            Assert.That(parameterless.StandardError, Is.EqualTo(status.StandardError));
            Assert.That(status.StandardOutput, Does.Contain(
                $"Workspace: {harness.WorkingDirectory}"));
            Assert.That(status.StandardOutput, Does.Contain("Location:  /"));
            Assert.That(status.StandardOutput, Does.Contain("Notebook:  (not selected)"));
            Assert.That(status.StandardOutput, Does.Contain("Status:    root"));
            Assert.That(status.StandardOutput, Does.Contain("mn use <notebook>"));
            Assert.That(File.Exists(WorkspaceConfigurationPath(harness)), Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies a current notebook reports its virtual location and ready state.</summary>
    [Test]
    public async Task Execute_SelectedNotebook_ReportsReadyState()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "project notes"));
        AssertSucceeded(await harness.RunAsync("use", "project notes"));
        var configurationPath = WorkspaceConfigurationPath(harness);
        var before = await File.ReadAllBytesAsync(configurationPath);

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Location:  /project notes"));
            Assert.That(result.StandardOutput, Does.Contain(
                "Notebook:  project notes.mnote"));
            Assert.That(result.StandardOutput, Does.Contain("Status:    ready"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Next:"));
            Assert.That(await File.ReadAllBytesAsync(configurationPath), Is.EqualTo(before));
        }
    }

    /// <summary>Verifies notebook metadata read-only state remains a successful status.</summary>
    [Test]
    public async Task Execute_ReadOnlyNotebook_ReportsReadOnlyState()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "archive"));
        AssertSucceeded(await harness.RunAsync("use", "archive"));
        await UpdateMetadataAsync(
            harness,
            "archive.mnote",
            new NotebookMetadataPatch().SetReadOnly(true));

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Status:    read-only"));
            Assert.That(result.StandardOutput, Does.Contain("Read operations are available"));
        }
    }

    /// <summary>Verifies a missing saved notebook is diagnosed without fallback.</summary>
    [Test]
    public async Task Execute_MissingNotebook_ReportsSavedVirtualLocation()
    {
        using var harness = new CliProcessHarness();
        await File.WriteAllTextAsync(
            WorkspaceConfigurationPath(harness),
            "format_version = 1\ncurrent_notebook = \"missing.mnote\"\n");

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Does.Contain("Location:  /missing"));
            Assert.That(result.StandardOutput, Does.Contain("Notebook:  missing.mnote"));
            Assert.That(result.StandardOutput, Does.Contain("Status:    missing"));
            Assert.That(result.StandardOutput, Does.Contain("mn use --root"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies corrupt and older notebooks are diagnosed as invalid.</summary>
    [TestCase(null)]
    [TestCase("0")]
    public async Task Execute_InvalidNotebook_ReportsValidationFailure(string? version)
    {
        using var harness = new CliProcessHarness();
        if (version == null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(harness.WorkingDirectory, "broken.mnote"),
                "not sqlite");
        }
        else
        {
            AssertSucceeded(await harness.RunAsync("create", "broken"));
            await UpdateMetadataAsync(
                harness,
                "broken.mnote",
                new NotebookMetadataPatch().SetVersion(version));
        }
        await File.WriteAllTextAsync(
            WorkspaceConfigurationPath(harness),
            "format_version = 1\ncurrent_notebook = \"broken.mnote\"\n");

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Does.Contain("Status:    invalid"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a newer recognizable notebook is reported as unsupported.</summary>
    [Test]
    public async Task Execute_NewerNotebook_ReportsUnsupportedVersion()
    {
        using var harness = new CliProcessHarness();
        AssertSucceeded(await harness.RunAsync("create", "future"));
        AssertSucceeded(await harness.RunAsync("use", "future"));
        await UpdateMetadataAsync(
            harness,
            "future.mnote",
            new NotebookMetadataPatch().SetVersion("2"));

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Does.Contain("Location:  /future"));
            Assert.That(result.StandardOutput, Does.Contain("Status:    unsupported"));
            Assert.That(result.StandardOutput, Does.Contain("version '2'"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies unusable configuration is diagnosed and preserved.</summary>
    [TestCase("not toml =", "invalid")]
    [TestCase("format_version = 2\n", "unsupported")]
    public async Task Execute_UnusableConfiguration_ReportsUnavailableSelection(
        string content,
        string expectedStatus)
    {
        using var harness = new CliProcessHarness();
        var configurationPath = WorkspaceConfigurationPath(harness);
        await File.WriteAllTextAsync(configurationPath, content);

        var result = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Does.Contain("Location:  (unavailable)"));
            Assert.That(result.StandardOutput, Does.Contain("Notebook:  (unavailable)"));
            Assert.That(result.StandardOutput, Does.Contain($"Status:    {expectedStatus}"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(await File.ReadAllTextAsync(configurationPath), Is.EqualTo(content));
        }
    }

    static async Task UpdateMetadataAsync(
        CliProcessHarness harness,
        string notebookFileName,
        NotebookMetadataPatch patch)
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqliteNotebookMetadataRepository(factory);
        await repository.UpdateAsync(
            Path.Combine(harness.WorkingDirectory, notebookFileName),
            patch,
            CancellationToken.None);
    }

    static string WorkspaceConfigurationPath(CliProcessHarness harness)
    {
        return Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
    }

    static void AssertSucceeded(CliProcessResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }
}
