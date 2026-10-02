using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies backup and restore wiring through the CLI process boundary.
/// </summary>
[TestFixture]
[Category("Process")]
public sealed class BackupRestoreWorkflowTests
{
    /// <summary>
    /// Verifies legacy restore only reports migration guidance and changes nothing.
    /// </summary>
    [Test]
    public async Task LegacyRestore_ReturnsGuidanceWithoutCreatingStateOrOutput()
    {
        using var harness = new CliProcessHarness();
        var inputPath = Path.Combine(harness.TemporaryDirectory, "legacy.zip");
        var input = "keep legacy archive"u8.ToArray();
        await File.WriteAllBytesAsync(inputPath, input);
        var outputDirectory = Path.Combine(harness.TemporaryDirectory, "restored");

        var restoreResult = await harness.RunAsync(
            "work",
            "restore",
            inputPath,
            "--output-dir",
            outputDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restoreResult.ExitCode,
                Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(restoreResult.StandardOutput, Is.Empty);
            Assert.That(restoreResult.StandardError, Does.Contain("mn notebooks restore"));
            Assert.That(await File.ReadAllBytesAsync(inputPath), Is.EqualTo(input));
            Assert.That(Directory.Exists(outputDirectory), Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    /// <summary>Verifies omitted legacy arguments still reach the migration stub.</summary>
    [Test]
    public async Task LegacyRestore_WithoutArguments_ReturnsNewCommandGuidance()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("work", "restore");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn notebooks restore"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies the legacy backup command only reports migration guidance and changes nothing.
    /// </summary>
    [Test]
    public async Task LegacyBackup_ReturnsGuidanceWithoutCreatingStateOrOutput()
    {
        using var harness = new CliProcessHarness();
        var outputPath = Path.Combine(harness.TemporaryDirectory, "legacy.zip");

        var result = await harness.RunAsync(
            "work",
            "backup",
            "anything",
            "--output",
            outputPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn notebooks backup"));
            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(harness.WorkingDirectory),
                Is.Empty);
        }
    }
}
