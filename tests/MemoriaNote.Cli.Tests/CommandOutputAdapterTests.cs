using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies CLI output formatting independently of the process console.</summary>
[TestFixture]
public sealed class CommandOutputAdapterTests
{
    /// <summary>Verifies prompt text can be written to standard error without a newline.</summary>
    [Test]
    public void WriteError_WritesOnlyToStandardErrorWithoutNewline()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);

        output.WriteError("Continue? [y/N] ");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(standardError.ToString(), Is.EqualTo("Continue? [y/N] "));
        }
    }

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
        var page = new PageListItem(
            pageId,
            "Meeting - Project A",
            new DateTimeOffset(2026, 9, 18, 1, 20, 30, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 4, 10, 0, TimeSpan.Zero));

        output.WritePageList(new[] { page }, longFormat: true);

        var lines = GetLines(standardOutput);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Contain("IDX"));
            Assert.That(lines[0], Does.Contain("PAGE-ID"));
            Assert.That(lines[0], Does.EndWith("NAME"));
            Assert.That(lines[1], Does.StartWith("1"));
            Assert.That(lines[1], Does.Contain("2026-09-18T01:20:30.0000000Z"));
            Assert.That(lines[1], Does.Contain("2026-09-20T04:10:00.0000000Z"));
            Assert.That(lines[1], Does.Contain("{}"));
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

        output.WritePageList(Array.Empty<PageListItem>(), longFormat);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    /// <summary>Verifies body-search output uses full IDs, escaped names, and span markers.</summary>
    [Test]
    public void WriteBodySearchHits_WithSnippet_WritesReleaseLayout()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var pageId = PageId.FromGuid(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        var snippet = new BodySearchCommandSnippet(
            "alpha 😀 beta alpha",
            new[]
            {
                new BodySearchCommandMatch(0, 5),
                new BodySearchCommandMatch(14, 5)
            });

        output.WriteBodySearchHits(new[]
        {
            new BodySearchCommandHit(
                pageId,
                "Meeting\nNotes\\draft",
                snippet)
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                GetLines(standardOutput),
                Is.EqualTo(new[]
                {
                    "01234567-89ab-cdef-0123-456789abcdef " +
                        "Meeting\\nNotes\\\\draft",
                    "  [[alpha]] 😀 beta [[alpha]]"
                }));
            Assert.That(standardError.ToString(), Is.Empty);
        }
    }

    /// <summary>Verifies marker-looking text remains distinct from generated markers.</summary>
    [Test]
    public void WriteBodySearchHits_WithLiteralMarkers_PreservesSnippetText()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);
        var snippet = new BodySearchCommandSnippet(
            "before [[alpha]] after",
            new[] { new BodySearchCommandMatch(9, 5) });

        output.WriteBodySearchHits(new[]
        {
            new BodySearchCommandHit(
                PageId.FromGuid(Guid.NewGuid()),
                "page",
                snippet)
        });

        Assert.That(
            GetLines(standardOutput)[1],
            Is.EqualTo("  before [[[[alpha]]]] after"));
        Assert.That(standardError.ToString(), Is.Empty);
    }

    /// <summary>Verifies no-snippet hits remain one line each.</summary>
    [Test]
    public void WriteBodySearchHits_WithoutSnippet_WritesOnlyIdentityLine()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var output = new ConsoleCommandOutput(standardOutput, standardError);

        output.WriteBodySearchHits(new[]
        {
            new BodySearchCommandHit(
                PageId.FromGuid(
                    Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210")),
                "page",
                null)
        });

        Assert.That(
            GetLines(standardOutput),
            Is.EqualTo(new[]
            {
                "fedcba98-7654-3210-fedc-ba9876543210 page"
            }));
        Assert.That(standardError.ToString(), Is.Empty);
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
                Is.EqualTo(
                    "Fatal: An unexpected error occurred." +
                    Environment.NewLine));
            Assert.That(standardError.ToString(), Does.Not.Contain("unavailable"));
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
                Is.EqualTo(
                    "Canceled: The operation was interrupted." +
                    Environment.NewLine));
        }
    }

    static PageListItem CreateSummary(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new PageListItem(
            PageId.FromGuid(Guid.NewGuid()),
            name,
            now,
            now);
    }

    static string[] GetLines(StringWriter writer)
    {
        return writer.ToString().Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }
}
