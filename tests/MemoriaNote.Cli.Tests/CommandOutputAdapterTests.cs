using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies CLI output formatting independently of the process console.</summary>
[TestFixture]
public sealed class CommandOutputAdapterTests
{
    /// <summary>Verifies that normal page output is line-oriented and does not truncate names.</summary>
    [Test]
    public void WritePageList_NormalFormat_WritesOnlyEscapedFullNames()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var longName = new string('x', 65);
        var pages = new[]
        {
            CreateSummary("Meeting\nNotes"),
            CreateSummary(longName)
        };

        output.WritePageList(pages, longFormat: false);

        Assert.That(
            GetLines(standardOutput),
            Is.EqualTo(new[] { "Meeting\\nNotes", longName }));
        Assert.That(standardError.ToString(), Is.Empty);
    }

    /// <summary>Verifies long output includes all page-summary metadata.</summary>
    [Test]
    public void WritePageList_LongFormat_WritesStableMetadataColumns()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var pageId = PageId.FromGuid(
            Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        var page = new PageSummary(
            NotebookId.FromDatabasePath(
                Path.Combine(Path.GetTempPath(), "output-adapter.db")),
            pageId,
            "Meeting - Project A",
            2,
            new Dictionary<string, string>
            {
                ["zeta"] = "last",
                ["alpha"] = "first"
            },
            "T",
            new DateTime(2026, 9, 18, 1, 20, 30, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 4, 10, 0, DateTimeKind.Utc),
            false);

        output.WritePageList(new[] { page }, longFormat: true);

        var lines = GetLines(standardOutput);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Contain("IDX"));
            Assert.That(lines[0], Does.Contain("PAGE-ID"));
            Assert.That(lines[0], Does.EndWith("NAME"));
            Assert.That(lines[1], Does.StartWith("2"));
            Assert.That(lines[1], Does.Contain("2026-09-18T01:20:30.0000000Z"));
            Assert.That(lines[1], Does.Contain("2026-09-20T04:10:00.0000000Z"));
            Assert.That(lines[1], Does.Contain("{\"alpha\":\"first\",\"zeta\":\"last\"}"));
            Assert.That(lines[1], Does.Contain(pageId.ToString()));
            Assert.That(lines[1], Does.EndWith("Meeting - Project A"));
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    /// <summary>Verifies an empty page list produces no headers or summary text.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void WritePageList_Empty_WritesNothing(bool longFormat)
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);

        output.WritePageList(Array.Empty<PageSummary>(), longFormat);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    /// <summary>Verifies that the shared executor preserves fatal error mapping.</summary>
    [Test]
    public async Task Execute_UnexpectedExceptionWritesFatalErrorAndReturnsFailure()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var executor = new CliCommandExecutor(
            output,
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            _ => Task.FromException<CliCommandResult>(
                new InvalidOperationException("unavailable")),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Unexpected));
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Fatal: unavailable" + Environment.NewLine));
        }
    }

    /// <summary>Verifies that cancellation is mapped before command execution.</summary>
    [Test]
    public async Task Execute_CanceledTokenWritesErrorAndReturnsCanceled()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var executor = new CliCommandExecutor(
            new ConsoleCommandOutput(standardOutput, standardError),
            new CliErrorMapper(),
            NullLogger<CliCommandExecutor>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executed = false;

        var result = await executor.ExecuteAsync(
            _ =>
            {
                executed = true;
                return CliCommandResult.Success();
            },
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(executed, Is.False);
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(
                standardError.ToString(),
                Is.EqualTo("Error: Operation was canceled" + Environment.NewLine));
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
