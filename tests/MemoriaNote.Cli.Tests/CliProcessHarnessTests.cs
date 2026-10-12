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
}
