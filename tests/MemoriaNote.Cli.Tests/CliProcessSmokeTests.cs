using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies a basic CLI command through the process boundary.</summary>
[TestFixture]
public sealed class CliProcessSmokeTests
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
                    "A lightweight, cross-platform CLI for workspace notebooks"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Terminal.Gui"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Temporarily unavailable"));
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }

        var visibleCommands = new[]
        {
            "cat",
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

    static bool ContainsCommand(string help, string command)
    {
        return help.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line => line.StartsWith(command + " ", StringComparison.Ordinal));
    }
}
