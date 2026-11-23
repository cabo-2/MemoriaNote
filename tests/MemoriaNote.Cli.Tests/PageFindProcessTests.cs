using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies body-search output across a clean process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class PageFindProcessTests
{
    /// <summary>Verifies final Scan presentation and Page ID cross the process boundary.</summary>
    [Test]
    public async Task Find_ProcessBoundary_WritesFinalSearchResult()
    {
        using var harness = new CliProcessHarness();
        var create = await harness.RunAsync("create", "search");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var databasePath = Path.Combine(harness.WorkingDirectory, "search.mnote");
        var pageId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        await SchemaV2TestNotebook.InsertPageAsync(
            databasePath,
            pageId,
            "Process result",
            "cross process marker");

        var result = await harness.RunAsync(
            "find",
            "marker",
            "--notebook",
            "search",
            "--no-snippet");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                result.StandardOutput,
                Is.EqualTo(
                    "01234567-89ab-cdef-0123-456789abcdef Process result" +
                    Environment.NewLine));
        }
    }
}
