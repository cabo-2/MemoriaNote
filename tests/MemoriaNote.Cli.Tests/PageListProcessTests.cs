using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies page-list streaming across a clean process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class PageListProcessTests
{
    /// <summary>Verifies more than one internal batch is emitted in stable order.</summary>
    [Test]
    public async Task Ls_ProcessBoundary_StreamsMultipleBatches()
    {
        using var harness = new CliProcessHarness();
        var create = await harness.RunAsync("create", "stream");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var databasePath = Path.Combine(harness.WorkingDirectory, "stream.mnote");
        await SchemaV2TestNotebook.InsertPagesAsync(databasePath, 260);

        var result = await harness.RunAsync(
            "ls",
            "--notebook",
            "stream",
            "--limit",
            "257");
        var lines = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(lines, Has.Length.EqualTo(257));
            Assert.That(lines[0], Is.EqualTo("page-0000"));
            Assert.That(lines[^1], Is.EqualTo("page-0256"));
        }
    }
}
