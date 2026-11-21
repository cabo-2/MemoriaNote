using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies page listing workflows through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class PageListIntegrationTests
{
    /// <summary>
    /// Verifies ls provides stable name ordering, limit, and read-only behavior.
    /// </summary>
    [Test]
    public async Task Ls_ListsStableNamesWithoutChangingState()
    {
        using var harness = new CliInProcessHarness();
        var create = await harness.RunAsync("create", "work.mnote");
        Assert.That(create.ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var repository = CreateRepository();
        await repository.CreatePageAsync(
            notebookPath,
            "alpha",
            "lower",
            null!,
            CancellationToken.None);
        await repository.CreatePageAsync(
            notebookPath,
            "Roadmap",
            "middle",
            null!,
            CancellationToken.None);
        await repository.CreatePageAsync(
            notebookPath,
            "Alpha",
            "upper",
            null!,
            CancellationToken.None);
        var before = await harness.ReadDatabaseBytesAsync(notebookPath);
        var workspaceConfigurationPath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var configurationBefore = await File.ReadAllBytesAsync(
            workspaceConfigurationPath);

        var limited = await harness.RunAsync("ls", "--limit", "2");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(limited.ExitCode, Is.Zero);
            Assert.That(limited.StandardError, Is.Empty);
            Assert.That(
                GetOutputLines(limited.StandardOutput),
                Is.EqualTo(new[] { "Alpha", "Roadmap" }));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                await harness.ReadDatabaseBytesAsync(notebookPath),
                Is.EqualTo(before));
            Assert.That(
                await File.ReadAllBytesAsync(workspaceConfigurationPath),
                Is.EqualTo(configurationBefore));
        }
    }

    /// <summary>
    /// Verifies a positional glob is applied before the selected sort, reverse, and limit.
    /// </summary>
    [Test]
    public async Task Ls_WithGlobSortReverseAndLimit_QueriesSchemaV2Pages()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        foreach (var name in new[] { "project-A", "project-B", "project-C", "other" })
        {
            await SchemaV2TestNotebook.CreatePageAsync(notebookPath, name, "body");
        }
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "project-A",
            DateTimeOffset.Parse("2026-01-03T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "project-B",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-03T00:00:00Z"));
        await SchemaV2TestNotebook.SetPageTimesAsync(
            notebookPath,
            "project-C",
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-02T00:00:00Z"));

        var updated = await harness.RunAsync(
            "ls",
            "project-?",
            "--sort",
            "updated",
            "--reverse",
            "--limit",
            "2");
        var created = await harness.RunAsync(
            "ls",
            "project-?",
            "--sort",
            "created");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.ExitCode, Is.Zero);
            Assert.That(updated.StandardError, Is.Empty);
            Assert.That(
                GetOutputLines(updated.StandardOutput),
                Is.EqualTo(new[] { "project-A", "project-C" }));
            Assert.That(created.ExitCode, Is.Zero);
            Assert.That(created.StandardError, Is.Empty);
            Assert.That(
                GetOutputLines(created.StandardOutput),
                Is.EqualTo(new[] { "project-A", "project-C", "project-B" }));
        }
    }

    /// <summary>Verifies MemoriaNote glob syntax remains distinct from shell and Unicode normalization.</summary>
    [Test]
    public async Task Ls_WithGlob_UsesLiteralClassCaseAndUnicodeScalarSemantics()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        foreach (var name in new[]
                 {
                     "Alpha", "alpha", "draft-*", "note-😀", "note-ab", "é", "e\u0301"
                 })
        {
            await SchemaV2TestNotebook.CreatePageAsync(notebookPath, name, "body");
        }

        var upper = await harness.RunAsync("ls", "[A-Z]*");
        var negatedUnix = await harness.RunAsync("ls", "[!a-z]*");
        var negatedSqlite = await harness.RunAsync("ls", "[^a-z]*");
        var literal = await harness.RunAsync("ls", "draft-\\*");
        var scalar = await harness.RunAsync("ls", "note-?");
        var composed = await harness.RunAsync("ls", "é");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetOutputLines(upper.StandardOutput), Is.EqualTo(new[] { "Alpha" }));
            Assert.That(
                GetOutputLines(negatedUnix.StandardOutput),
                Is.EqualTo(new[] { "Alpha", "é" }));
            Assert.That(negatedSqlite.StandardOutput, Is.EqualTo(negatedUnix.StandardOutput));
            Assert.That(GetOutputLines(literal.StandardOutput), Is.EqualTo(new[] { "draft-*" }));
            Assert.That(GetOutputLines(scalar.StandardOutput), Is.EqualTo(new[] { "note-😀" }));
            Assert.That(GetOutputLines(composed.StandardOutput), Is.EqualTo(new[] { "é" }));
            Assert.That(upper.StandardError, Is.Empty);
            Assert.That(negatedUnix.StandardError, Is.Empty);
            Assert.That(negatedSqlite.StandardError, Is.Empty);
            Assert.That(literal.StandardError, Is.Empty);
            Assert.That(scalar.StandardError, Is.Empty);
            Assert.That(composed.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies omitted and wildcard-only patterns have the same match-all behavior.</summary>
    [Test]
    public async Task Ls_WithWildcardOnlyPattern_MatchesTheOmittedPattern()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        await SchemaV2TestNotebook.CreatePageAsync(notebookPath, "Alpha", "body");
        await SchemaV2TestNotebook.CreatePageAsync(notebookPath, "beta", "body");

        var omitted = await harness.RunAsync("ls");
        var wildcard = await harness.RunAsync("ls", "**");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wildcard.ExitCode, Is.Zero);
            Assert.That(wildcard.StandardError, Is.Empty);
            Assert.That(wildcard.StandardOutput, Is.EqualTo(omitted.StandardOutput));
        }
    }

    /// <summary>Verifies empty and invalid patterns use classified diagnostics without provider details.</summary>
    [Test]
    public async Task Ls_WithInvalidGlob_ReturnsValidationFailure()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);

        var empty = await harness.RunAsync("ls", string.Empty);
        var invalid = await harness.RunAsync("ls", "[z-a]");
        var tooLong = await harness.RunAsync("ls", new string('a', 513));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(empty.StandardOutput, Is.Empty);
            Assert.That(
                empty.StandardError,
                Is.EqualTo(
                    "Error: A page-name pattern must not be empty." +
                    Environment.NewLine));
            Assert.That(invalid.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(invalid.StandardOutput, Is.Empty);
            Assert.That(
                invalid.StandardError,
                Is.EqualTo(
                    "Error: The page-name pattern is not valid MemoriaNote glob syntax." +
                    Environment.NewLine));
            Assert.That(invalid.StandardError, Does.Not.Contain("SQLite"));
            Assert.That(tooLong.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(tooLong.StandardOutput, Is.Empty);
            Assert.That(
                tooLong.StandardError,
                Is.EqualTo(
                    "Error: A page-name pattern must contain at most 512 Unicode scalars." +
                    Environment.NewLine));
        }
    }

    /// <summary>Verifies long output exposes complete page metadata including the full Page ID.</summary>
    [Test]
    public async Task Ls_LongFormat_WritesAllSummaryColumns()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var page = await CreateRepository().CreatePageAsync(
            notebookPath,
            "Meeting - Project A",
            "body",
            "projects",
            CancellationToken.None);

        var result = await harness.RunAsync("ls", "--long");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(result.StandardOutput, Does.Contain("CREATED-UTC"));
            Assert.That(result.StandardOutput, Does.Contain("UPDATED-UTC"));
            Assert.That(result.StandardOutput, Does.Contain("PAGE-ID"));
            Assert.That(result.StandardOutput, Does.Contain(page.Guid.ToString("D")));
            Assert.That(result.StandardOutput, Does.Contain("{}"));
            Assert.That(result.StandardOutput, Does.EndWith(
                "Meeting - Project A" + Environment.NewLine));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies long output writes one header while streaming multiple batches.</summary>
    [Test]
    public async Task Ls_LongFormatAcrossBatches_WritesOneHeader()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "work.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("use", "work")).ExitCode, Is.Zero);
        var notebookPath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        await SchemaV2TestNotebook.InsertPagesAsync(notebookPath, 130);

        var result = await harness.RunAsync("ls", "--long");
        var lines = GetOutputLines(result.StandardOutput);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(lines, Has.Length.EqualTo(131));
            Assert.That(lines.Count(line => line.StartsWith("IDX ", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(lines[1], Does.EndWith("page-0000"));
            Assert.That(lines[^1], Does.EndWith("page-0129"));
        }
    }

    /// <summary>Verifies an explicit notebook disambiguates multiple root notebooks.</summary>
    [Test]
    public async Task Ls_WithMultipleNotebooks_RequiresExplicitTarget()
    {
        using var harness = new CliInProcessHarness();
        Assert.That((await harness.RunAsync("create", "first.mnote")).ExitCode, Is.Zero);
        Assert.That((await harness.RunAsync("create", "second.mnote")).ExitCode, Is.Zero);
        await CreateRepository().CreatePageAsync(
            Path.Combine(harness.WorkingDirectory, "second.mnote"),
            "Second page",
            "body",
            null!,
            CancellationToken.None);

        var ambiguous = await harness.RunAsync("ls");
        var explicitResult = await harness.RunAsync(
            "ls",
            "--notebook",
            "second.mnote");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(ambiguous.StandardOutput, Is.Empty);
            Assert.That(ambiguous.StandardError, Does.Contain("--notebook"));
            Assert.That(explicitResult.ExitCode, Is.Zero);
            Assert.That(
                GetOutputLines(explicitResult.StandardOutput),
                Is.EqualTo(new[] { "Second page" }));
            Assert.That(explicitResult.StandardError, Is.Empty);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies the retired dynamic-completion option is not exposed by ls.</summary>
    [Test]
    public async Task Ls_CompletionOption_IsUnavailable()
    {
        using var harness = new CliInProcessHarness();

        var help = await harness.RunAsync("ls", "--help");
        var completion = await harness.RunAsync("ls", "--completion");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(help.ExitCode, Is.Zero);
            Assert.That(help.StandardOutput, Does.Contain("Usage: mn ls"));
            Assert.That(help.StandardOutput, Does.Contain("[<page-name-pattern>]"));
            Assert.That(help.StandardOutput, Does.Contain("--sort <mode>"));
            Assert.That(help.StandardOutput, Does.Contain("-r|--reverse"));
            Assert.That(help.StandardOutput, Does.Contain("--limit <count>"));
            Assert.That(help.StandardOutput, Does.Contain("-l|--long"));
            Assert.That(help.StandardOutput, Does.Contain("case-sensitively"));
            Assert.That(help.StandardOutput, Does.Contain("Unicode normalization"));
            Assert.That(help.StandardOutput, Does.Contain("[!0-9]"));
            Assert.That(help.StandardOutput, Does.Contain("[^0-9]"));
            Assert.That(help.StandardOutput, Does.Contain("mn ls 'project-*'"));
            Assert.That(help.StandardOutput, Does.Not.Contain("--completion"));
            Assert.That(completion.ExitCode, Is.Not.Zero);
        }
    }

    static SchemaV2TestPageRepository CreateRepository()
    {
        return new SchemaV2TestPageRepository();
    }

    static string[] GetOutputLines(string output)
    {
        return output.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }
    /// <summary>
    /// Verifies ls reports a missing selection without creating configuration.
    /// </summary>
    [Test]
    public async Task Ls_WithoutSelection_ReturnsConflict()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("ls");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("mn use"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }
}
