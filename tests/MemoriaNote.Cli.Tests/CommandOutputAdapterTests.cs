using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies CLI output formatting independently of the process console.</summary>
[TestFixture]
public sealed class CommandOutputAdapterTests
{
    /// <summary>Verifies that page completion remains sorted and line-oriented.</summary>
    [Test]
    public void WritePageCompletion_SortsLowercasesAndRemovesDuplicates()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var pages = new[]
        {
            CreateSummary("Release Notes"),
            CreateSummary("meeting Agenda"),
            CreateSummary("Meeting Notes")
        };

        output.WritePageCompletion(pages, pages.Length);

        Assert.That(
            GetLines(standardOutput),
            Is.EqualTo(new[] { "meeting", "release" }));
        Assert.That(standardError.ToString(), Is.Empty);
    }

    /// <summary>Verifies the bounded page table and overflow notice.</summary>
    [Test]
    public void WritePageList_TruncatesLongNamesAndReportsTotalCount()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var longName = new string('x', 65);

        output.WritePageList(new[] { CreateSummary(longName) }, 2);

        var rendered = standardOutput.ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rendered, Does.Contain(new string('x', 64)));
            Assert.That(rendered, Does.Not.Contain(longName));
            Assert.That(
                rendered,
                Does.Contain("Number of text messages exceeds 1000"));
            Assert.That(rendered, Does.Contain("Total count: 2"));
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    /// <summary>Verifies that the shared executor preserves fatal error mapping.</summary>
    [Test]
    public void Execute_UnexpectedExceptionWritesFatalErrorAndReturnsFailure()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            NullLogger<CliCommandExecutor>.Instance);

        var result = executor.Execute(
            () => throw new InvalidOperationException("unavailable"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(-1));
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Fatal: unavailable" + Environment.NewLine));
        }
    }

    static PageSummary CreateSummary(string name)
    {
        var now = DateTime.UtcNow;
        return new PageSummary(
            NotebookId.FromDatabasePath(
                Path.Combine(Path.GetTempPath(), "output-adapter.db")),
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            "text/plain",
            now,
            now,
            false);
    }

    static string[] GetLines(StringWriter writer)
    {
        return writer.ToString().Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }
}
