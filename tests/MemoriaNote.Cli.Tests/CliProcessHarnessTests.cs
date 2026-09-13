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
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }

        var publicCommands = new[]
        {
            "config",
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
    /// Verifies that parser errors use standard error and return failure.
    /// </summary>
    [Test]
    public async Task UnknownCommand_WritesDiagnosticAndReturnsFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("unknown-command");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
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
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("Error: No name"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
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
            Assert.That(result.ExitCode, Is.Not.Zero);
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
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.StartWith("Fatal: "));
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

    static bool ContainsCommand(string help, string command)
    {
        return help.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line => line.StartsWith(command + " ", StringComparison.Ordinal));
    }
}
