using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies CLI startup, exit codes, and separated text streams.
/// </summary>
[TestFixture]
[Category("Process")]
public sealed class ProcessInvocationTests
{
    /// <summary>
    /// Verifies version output uses the assembly informational version without runtime state.
    /// </summary>
    [Test]
    public async Task Version_WritesInformationalVersionWithoutRuntimeState()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("--version");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                result.StandardOutput,
                Is.EqualTo(CliProductVersion.Display + Environment.NewLine));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(Directory.Exists(harness.ApplicationDataDirectory), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.TemporaryDirectory),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that root help is written to standard output by a successful process.
    /// </summary>
    [Test]
    public async Task Help_WritesPublicCommandsToStandardOutputAndReturnsSuccess()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                result.StandardOutput,
                Does.Contain(
                    "A lightweight, cross-platform CLI for workspace notebooks"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Terminal.Gui"));
            Assert.That(
                result.StandardOutput,
                Does.Not.Contain("Temporarily unavailable"));
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn"));
            Assert.That(result.StandardOutput, Does.Contain("--version"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }

        var visibleCommands = new[]
        {
            "cat",
            "completion",
            "config",
            "create",
            "edit",
            "ls",
            "new",
            "rename",
            "use"
        };
        foreach (var command in visibleCommands)
        {
            Assert.That(
                ContainsCommand(result.StandardOutput, command),
                Is.True,
                $"Root help did not describe the '{command}' command.");
        }

        var hiddenCommands = new[]
        {
            "export",
            "find",
            "import",
            "list",
            "page",
            "work"
        };
        foreach (var command in hiddenCommands)
        {
            Assert.That(
                ContainsCommand(result.StandardOutput, command),
                Is.False,
                $"Root help described the hidden '{command}' command.");
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
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardError, Does.StartWith("Error:"));
            Assert.That(
                result.StandardError,
                Does.Contain("Unrecognized command or argument 'unknown-command'"));
            Assert.That(result.StandardError, Does.Contain("--help"));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
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
