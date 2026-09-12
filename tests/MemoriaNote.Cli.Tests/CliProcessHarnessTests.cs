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
    /// Verifies that the built CLI can run to completion and its output can be captured.
    /// </summary>
    [Test]
    public async Task Help_CanRunOutOfProcess()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.StandardOutput, Is.Not.Empty);
            Assert.That(result.StandardError, Is.Not.Null);
            Assert.That(result.ExitCode, Is.GreaterThanOrEqualTo(0));
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
}
