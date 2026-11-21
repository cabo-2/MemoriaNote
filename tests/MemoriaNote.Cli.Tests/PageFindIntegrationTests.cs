using MemoriaNote.Cli.Tests.Infrastructure;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies final body-search workflows through the production CLI.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class PageFindIntegrationTests
{
    /// <summary>Verifies literal AND, case modes, snippets, Page IDs, and read-only behavior.</summary>
    [Test]
    public async Task Find_WithKeywords_UsesFinalScanPresentationAndPageIdWorkflow()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var alpha = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Alpha",
            "JavaScript migration alpha");
        var beta = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Beta",
            "javascript migration beta");
        await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Gamma",
            "JavaScript without the other keyword");
        var databaseBefore = await harness.ReadDatabaseBytesAsync(notebookPath);
        var workspaceConfigurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var configurationBefore = await File.ReadAllBytesAsync(
            workspaceConfigurationPath);

        var result = await harness.RunAsync("find", "JavaScript", "migration");
        var quoted = await harness.RunAsync("find", "JavaScript migration");
        var sensitive = await harness.RunAsync(
            "find",
            "JavaScript",
            "migration",
            "--case-sensitive",
            "--no-snippet");
        var cat = await harness.RunAsync("cat", "--id", alpha.PageId.ToString());

        var expectedDefault = new[]
        {
            $"{alpha.PageId} Alpha",
            "  [[JavaScript]] [[migration]] alpha",
            $"{beta.PageId} Beta",
            "  [[javascript]] [[migration]] beta"
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(OutputLines(result.StandardOutput), Is.EqualTo(expectedDefault));
            Assert.That(quoted.ExitCode, Is.Zero);
            Assert.That(quoted.StandardOutput, Is.EqualTo(result.StandardOutput));
            Assert.That(quoted.StandardError, Is.Empty);
            Assert.That(sensitive.ExitCode, Is.Zero);
            Assert.That(sensitive.StandardError, Is.Empty);
            Assert.That(
                OutputLines(sensitive.StandardOutput),
                Is.EqualTo(new[] { $"{alpha.PageId} Alpha" }));
            Assert.That(cat.ExitCode, Is.Zero);
            Assert.That(cat.StandardOutput, Is.EqualTo("JavaScript migration alpha"));
            Assert.That(
                await harness.ReadDatabaseBytesAsync(notebookPath),
                Is.EqualTo(databaseBefore));
            Assert.That(
                await File.ReadAllBytesAsync(workspaceConfigurationPath),
                Is.EqualTo(configurationBefore));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies updated ordering, bounded output, HasMore guidance, and no-hit success.</summary>
    [Test]
    public async Task Find_WithSortAndLimit_ReportsRemainingResults()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var alpha = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Alpha",
            "bounded hit");
        var beta = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Beta",
            "bounded hit");
        var gamma = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Gamma",
            "bounded hit");
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "Alpha",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "Beta",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-03T00:00:00Z"));
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "Gamma",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-02T00:00:00Z"));

        var limited = await harness.RunAsync(
            "find",
            "hit",
            "--sort",
            "updated",
            "--limit",
            "2",
            "--no-snippet");
        var noHit = await harness.RunAsync("find", "absent");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(limited.ExitCode, Is.Zero);
            Assert.That(
                OutputLines(limited.StandardOutput),
                Is.EqualTo(new[]
                {
                    $"{beta.PageId} Beta",
                    $"{gamma.PageId} Gamma"
                }));
            Assert.That(
                limited.StandardError,
                Is.EqualTo(
                    "More results are available; increase --limit to show more " +
                    "(maximum 1000)." + Environment.NewLine));
            Assert.That(noHit.ExitCode, Is.Zero);
            Assert.That(noHit.StandardOutput, Is.Empty);
            Assert.That(noHit.StandardError, Is.Empty);
            Assert.That(alpha.PageId, Is.Not.EqualTo(beta.PageId));
        }
    }

    /// <summary>Verifies short keywords and canonical Unicode forms remain literal.</summary>
    [Test]
    public async Task Find_WithShortAndUnicodeKeywords_DoesNotNormalizeOrFallback()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var shortPage = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Short",
            "x yz");
        var composed = await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Composed",
            "é");
        await SchemaV2TestNotebook.CreatePageAsync(
            notebookPath,
            "Decomposed",
            "e\u0301");

        var shortResult = await harness.RunAsync(
            "find",
            "x",
            "yz",
            "--no-snippet");
        var unicode = await harness.RunAsync("find", "é", "--no-snippet");
        var operatorLiteral = await harness.RunAsync("find", "AND", "--no-snippet");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shortResult.ExitCode, Is.Zero);
            Assert.That(
                OutputLines(shortResult.StandardOutput),
                Is.EqualTo(new[] { $"{shortPage.PageId} Short" }));
            Assert.That(unicode.ExitCode, Is.Zero);
            Assert.That(
                OutputLines(unicode.StandardOutput),
                Is.EqualTo(new[] { $"{composed.PageId} Composed" }));
            Assert.That(operatorLiteral.ExitCode, Is.Zero);
            Assert.That(operatorLiteral.StandardOutput, Is.Empty);
            Assert.That(shortResult.StandardError, Is.Empty);
            Assert.That(unicode.StandardError, Is.Empty);
            Assert.That(operatorLiteral.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies parser and Application validation expose safe final diagnostics.</summary>
    [Test]
    public async Task Find_WithInvalidInput_ReturnsValidationWithoutSearchingAllPages()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);

        var missing = await harness.RunAsync("find");
        var empty = await harness.RunAsync("find", string.Empty);
        var invalidSort = await harness.RunAsync("find", "word", "--sort", "relevance");
        var invalidLimit = await harness.RunAsync("find", "word", "--limit", "1001");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(missing.StandardError, Does.Contain("mn ls"));
            Assert.That(empty.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                empty.StandardError,
                Is.EqualTo(
                    "Error: A body-search query must contain at least one keyword." +
                    Environment.NewLine));
            Assert.That(invalidSort.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                invalidSort.StandardError,
                Is.EqualTo("Error: Sort must be name or updated." + Environment.NewLine));
            Assert.That(invalidLimit.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                invalidLimit.StandardError,
                Is.EqualTo(
                    "Error: Limit must be between 1 and 1000." +
                    Environment.NewLine));
            Assert.That(missing.StandardOutput, Is.Empty);
            Assert.That(empty.StandardOutput, Is.Empty);
            Assert.That(invalidSort.StandardOutput, Is.Empty);
            Assert.That(invalidLimit.StandardOutput, Is.Empty);
        }
    }

    /// <summary>Verifies final find help describes release semantics and omits validation controls.</summary>
    [Test]
    public async Task Find_Help_DescribesFinalReleaseContract()
    {
        using var harness = new CliInProcessHarness();

        var help = await harness.RunAsync("find", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardError, Is.Empty);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn find"));
            Assert.That(help.StandardOutput, Does.Contain("--sort <mode>"));
            Assert.That(help.StandardOutput, Does.Contain("default is name"));
            Assert.That(help.StandardOutput, Does.Contain("default 20, maximum"));
            Assert.That(help.StandardOutput, Does.Contain("1000"));
            Assert.That(help.StandardOutput, Does.Contain("--case-sensitive"));
            Assert.That(help.StandardOutput, Does.Contain("--no-snippet"));
            Assert.That(help.StandardOutput, Does.Contain("literal AND"));
            Assert.That(help.StandardOutput, Does.Contain("shell quoting"));
            Assert.That(help.StandardOutput, Does.Contain("searched literally"));
            Assert.That(help.StandardOutput, Does.Contain("One- and two-scalar"));
            Assert.That(help.StandardOutput, Does.Contain("Unicode normalization"));
            Assert.That(help.StandardOutput, Does.Contain("[[ and ]]"));
            Assert.That(help.StandardOutput, Does.Contain("mn cat --id <page-id>"));
            Assert.That(help.StandardOutput, Does.Contain("mn edit --id <page-id>"));
            Assert.That(help.StandardOutput, Does.Not.Contain("--method"));
            Assert.That(help.StandardOutput, Does.Not.Contain("--batch-size"));
            Assert.That(help.StandardOutput, Does.Not.Contain("relevance"));
        }
    }

    static string[] OutputLines(string output) => output.Split(
        new[] { '\r', '\n' },
        StringSplitOptions.RemoveEmptyEntries);
}
