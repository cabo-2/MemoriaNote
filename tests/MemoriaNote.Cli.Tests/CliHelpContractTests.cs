using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies help and parser metadata across the complete command tree.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class CliHelpContractTests
{
    static readonly string[] CommandPaths =
    {
        string.Empty,
        "create",
        "use",
        "status",
        "notebooks",
        "notebooks list",
        "notebooks backup",
        "notebooks restore",
        "notebooks import",
        "notebooks export",
        "notebooks metadata",
        "edit",
        "new",
        "config",
        "config path",
        "config show",
        "config validate",
        "config editor",
        "config editor setup",
        "config editor show",
        "config editor unset",
        "completion",
        "completion zsh",
        "ls",
        "cat",
        "rename",
        "delete"
    };

    /// <summary>Verifies every public command exposes successful, stdout-only help.</summary>
    [TestCaseSource(nameof(CommandPaths))]
    public async Task Help_ForEveryCommand_UsesTheCompleteCommandPath(
        string commandPath)
    {
        using var harness = new CliInProcessHarness();
        var arguments = commandPath.Length == 0
            ? new[] { "--help" }
            : commandPath.Split(' ').Append("--help").ToArray();

        var result = await harness.RunAsync(arguments);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(
                result.StandardOutput,
                Does.Contain("Usage: mn" +
                    (commandPath.Length == 0 ? string.Empty : " " + commandPath)));
            Assert.That(result.StandardOutput, Does.Not.Contain("Syntax:"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies workspace selection is not advertised by user-config commands.</summary>
    [TestCase("config")]
    [TestCase("config path")]
    [TestCase("config show")]
    [TestCase("config validate")]
    [TestCase("config editor")]
    [TestCase("config editor setup")]
    [TestCase("config editor show")]
    [TestCase("config editor unset")]
    public async Task Help_ForConfigCommands_OmitsWorkspaceOption(string commandPath)
    {
        using var harness = new CliInProcessHarness();
        var arguments = commandPath.Split(' ').Append("--help").ToArray();

        var result = await harness.RunAsync(arguments);

        Assert.That(result.StandardOutput, Does.Not.Contain("--workspace"));
    }

    /// <summary>Verifies subcommand guidance contains the executable and full path.</summary>
    [TestCase("notebooks", "mn notebooks")]
    [TestCase("config", "mn config")]
    [TestCase("config editor", "mn config editor")]
    [TestCase("completion", "mn completion")]
    public async Task Help_ForCommandGroups_UsesCompletePathInFooter(
        string commandPath,
        string expectedPath)
    {
        using var harness = new CliInProcessHarness();
        var arguments = commandPath.Split(' ').Append("--help").ToArray();

        var result = await harness.RunAsync(arguments);

        Assert.That(
            result.StandardOutput,
            Does.Contain($"Run '{expectedPath} [command] --help'"));
    }

    /// <summary>Verifies help states each command's alternative argument forms.</summary>
    [TestCase("use", "Usage: mn use [options] (<notebook> | --root)")]
    [TestCase("notebooks backup", "Usage: mn notebooks backup [<archive>] [options]")]
    [TestCase("notebooks restore", "Usage: mn notebooks restore [<archive>] --target <notebook> [options]")]
    [TestCase("edit", "Usage: mn edit [options] (<page-name> | --id <uuid-or-prefix>)")]
    [TestCase("cat", "Usage: mn cat [options] (<page-name> | --id <uuid-or-prefix>)")]
    [TestCase("rename", "mn rename [options] --id <uuid-or-prefix> <new-name>")]
    [TestCase("delete", "Usage: mn delete [options] (<page-name> | --id <uuid-or-prefix>)")]
    public async Task Help_ForAlternativeInputs_StatesAcceptedSyntax(
        string commandPath,
        string expectedUsage)
    {
        using var harness = new CliInProcessHarness();
        var arguments = commandPath.Split(' ').Append("--help").ToArray();

        var result = await harness.RunAsync(arguments);

        Assert.That(result.StandardOutput, Does.Contain(expectedUsage));
    }
}
