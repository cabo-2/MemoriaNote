using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook checking across the published process boundary.</summary>
[TestFixture]
[Category("Process")]
[NonParallelizable]
public sealed class NotebookCheckProcessTests
{
    /// <summary>Verifies a separate mn process checks a schema v2 notebook read-only.</summary>
    [Test]
    public async Task Check_ValidNotebook_FromSeparateProcessSucceeds()
    {
        using var harness = new CliProcessHarness();
        var create = await harness.RunAsync("create", "process-check");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var source = Path.Combine(harness.WorkingDirectory, "process-check.mnote");
        var beforeBytes = await File.ReadAllBytesAsync(source);
        var beforeFiles = Directory.GetFileSystemEntries(harness.WorkingDirectory);

        var result = await harness.RunAsync("notebooks", "check", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Notebook check: valid"));
            Assert.That(result.StandardOutput, Does.Contain("derived-data: passed"));
            Assert.That(result.StandardOutput, Does.Contain("Source modified: no"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
            Assert.That(
                Directory.GetFileSystemEntries(harness.WorkingDirectory),
                Is.EqualTo(beforeFiles));
        }
    }
}
