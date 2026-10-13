using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies parser-only contracts through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class CliParserIntegrationTests
{
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
}
