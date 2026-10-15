using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies parser-only contracts through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class CliParserIntegrationTests
{
    /// <summary>Verifies required and mutually exclusive inputs fail before execution.</summary>
    [TestCase("create", "notebook is required")]
    [TestCase("use", "exactly one of <notebook> or --root")]
    [TestCase("notebooks restore", "--target <notebook>")]
    [TestCase("notebooks import", "import directory")]
    [TestCase("notebooks export", "export directory")]
    [TestCase("edit", "exactly one of <page-name> or --id")]
    [TestCase("new", "A page name is required")]
    [TestCase("cat", "exactly one of <page-name> or --id")]
    [TestCase("rename", "either <page-name> <new-name> or --id")]
    [TestCase("delete --force", "exactly one of <page-name> or --id")]
    public async Task MissingRequiredInput_ReturnsParserValidation(
        string arguments,
        string expectedDiagnostic)
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync(arguments.Split(' '));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain(expectedDiagnostic).IgnoreCase);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies config does not accept a workspace option it does not use.</summary>
    [Test]
    public async Task Config_WithWorkspaceOption_IsUnrecognized()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync(
            "config",
            "--workspace",
            harness.WorkingDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                result.StandardError,
                Does.StartWith("Error: Unrecognized option '--workspace'"));
            Assert.That(result.StandardError, Does.Contain("--help"));
            Assert.That(result.StandardOutput, Is.Empty);
        }
    }

    /// <summary>Verifies removed legacy commands are unknown and have no side effects.</summary>
    [TestCase("work")]
    [TestCase("import")]
    [TestCase("export")]
    [TestCase("list")]
    [TestCase("find")]
    public async Task RemovedLegacyCommand_IsUnrecognizedWithoutCreatingConfiguration(
        string command)
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync(command);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                result.StandardError,
                Does.StartWith(
                    $"Error: Unrecognized command or argument '{command}'"));
            Assert.That(result.StandardError, Does.Contain("--help"));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }
}
