using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook repair across the published process boundary.</summary>
[TestFixture]
[Category("Process")]
[NonParallelizable]
public sealed class NotebookRepairProcessTests
{
    /// <summary>Verifies a separate mn process publishes a checkable repaired notebook.</summary>
    [Test]
    public async Task Repair_ValidNotebook_FromSeparateProcessSucceeds()
    {
        using var harness = new CliProcessHarness();
        var create = await harness.RunAsync("create", "process-repair");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var source = Path.Combine(harness.WorkingDirectory, "process-repair.mnote");
        var output = Path.Combine(harness.WorkingDirectory, "repaired.mnote");
        var beforeBytes = await File.ReadAllBytesAsync(source);

        var repair = await harness.RunAsync(
            "notebooks",
            "repair",
            source,
            "--output",
            output);
        var check = await harness.RunAsync("notebooks", "check", output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repair.ExitCode, Is.Zero, repair.StandardError);
            Assert.That(repair.StandardOutput, Does.Contain("Notebook repair: completed"));
            Assert.That(repair.StandardOutput, Does.Contain("Output published: yes"));
            Assert.That(repair.StandardError, Is.Empty);
            Assert.That(check.ExitCode, Is.Zero, check.StandardError);
            Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(beforeBytes));
        }
    }
}
