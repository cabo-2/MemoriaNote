using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies safe flat text export through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookExportProcessTests
{
    /// <summary>Verifies help documents the destination, target, and conflict policy.</summary>
    [Test]
    public async Task Help_DescribesExportContract()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("notebooks", "export", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks export"));
            Assert.That(result.StandardOutput, Does.Contain("<directory>"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--name-conflict <policy>"));
            Assert.That(result.StandardOutput, Does.Contain("UTF-8"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies selected-notebook export is flat, reversible, and summarized.</summary>
    [Test]
    public async Task Export_SelectedNotebook_CreatesFlatUtf8FilesAndSummary()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "selected");
        var repository = CreatePageRepository();
        const string expected = "Café\r\n日本語\n";
        await repository.CreatePageAsync(
            notebookPath,
            "Plan/2026",
            expected,
            "ignored/path",
            CancellationToken.None);
        var destination = Path.Combine(harness.TemporaryDirectory, "selected-export");

        var result = await harness.RunAsync("notebooks", "export", destination);

        var outputPath = Path.Combine(destination, "Plan%2F2026~mn~2~.txt");
        var bytes = await File.ReadAllBytesAsync(outputPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Export completed: "));
            Assert.That(result.StandardOutput, Does.Contain($"source=\"{notebookPath}\""));
            Assert.That(result.StandardOutput, Does.Contain($"destination=\"{destination}\""));
            Assert.That(result.StandardOutput, Does.Match(@"exported=1\r?\n$"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(Directory.GetDirectories(destination), Is.Empty);
            Assert.That(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), Is.False);
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies an explicit notebook overrides current selection without changing it.</summary>
    [Test]
    public async Task Export_ExplicitNotebook_UsesSharedResolverWithoutChangingCurrent()
    {
        using var harness = new CliProcessHarness();
        await CreateAndSelectNotebookAsync(harness, "current");
        var explicitCreate = await harness.RunAsync("create", "explicit");
        Assert.That(explicitCreate.ExitCode, Is.Zero, explicitCreate.StandardError);
        var explicitPath = Path.Combine(harness.WorkingDirectory, "explicit.mnote");
        await CreatePageRepository().CreatePageAsync(
            explicitPath,
            "Only explicit",
            "explicit text",
            null,
            CancellationToken.None);
        var destination = Path.Combine(harness.TemporaryDirectory, "explicit-export");

        var result = await harness.RunAsync(
            "notebooks",
            "export",
            destination,
            "--notebook",
            "explicit");
        var status = await harness.RunAsync("status");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain($"source=\"{explicitPath}\""));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(destination, "Only explicit.txt")),
                Is.EqualTo("explicit text"));
            Assert.That(status.StandardOutput, Does.Contain("current.mnote"));
        }
    }

    /// <summary>Verifies duplicate names can be exported with stable full Page ID suffixes.</summary>
    [Test]
    public async Task Export_IdSuffixPolicy_ExportsEveryDuplicatePage()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "duplicates");
        var repository = CreatePageRepository();
        var first = await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "first",
            null,
            CancellationToken.None);
        var second = await repository.CreatePageAsync(
            notebookPath,
            "Daily",
            "second",
            null,
            CancellationToken.None);
        var destination = Path.Combine(harness.TemporaryDirectory, "duplicate-export");

        var failed = await harness.RunAsync("notebooks", "export", destination);
        var succeeded = await harness.RunAsync(
            "notebooks",
            "export",
            destination,
            "--name-conflict",
            "id-suffix");

        var firstName = "Daily~id~" + first.Guid.ToString("D").ToLowerInvariant() + ".txt";
        var secondName = "Daily~id~" + second.Guid.ToString("D").ToLowerInvariant() + ".txt";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(failed.StandardOutput, Is.Empty);
            Assert.That(failed.StandardError, Does.Contain("unique, safe file paths"));
            Assert.That(succeeded.ExitCode, Is.Zero, succeeded.StandardError);
            Assert.That(succeeded.StandardOutput, Does.Contain("exported=2"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, firstName)), Is.EqualTo("first"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, secondName)), Is.EqualTo("second"));
        }
    }

    /// <summary>Verifies an existing path is rejected before notebook resolution and preserved.</summary>
    [Test]
    public async Task Export_ExistingDestination_ReturnsConflictWithoutResolvingNotebook()
    {
        using var harness = new CliProcessHarness();
        var destination = Path.Combine(harness.TemporaryDirectory, "existing");
        Directory.CreateDirectory(destination);
        var existingPath = Path.Combine(destination, "keep.txt");
        await File.WriteAllTextAsync(existingPath, "keep");

        var result = await harness.RunAsync("notebooks", "export", destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already exists"));
            Assert.That(await File.ReadAllTextAsync(existingPath), Is.EqualTo("keep"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies an invalid policy fails before resolving a current notebook.</summary>
    [Test]
    public async Task Export_InvalidNameConflictPolicy_ReturnsValidationBeforeTargetResolution()
    {
        using var harness = new CliProcessHarness();
        var destination = Path.Combine(harness.TemporaryDirectory, "invalid-policy");

        var result = await harness.RunAsync(
            "notebooks",
            "export",
            destination,
            "--name-conflict",
            "rename");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("fail or id-suffix"));
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    static async Task<string> CreateAndSelectNotebookAsync(
        CliProcessHarness harness,
        string name)
    {
        var create = await harness.RunAsync("create", name);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var use = await harness.RunAsync("use", name);
        Assert.That(use.ExitCode, Is.Zero, use.StandardError);
        return Path.Combine(harness.WorkingDirectory, name + ".mnote");
    }

    static SqlitePageRepository CreatePageRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
