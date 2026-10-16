using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies flat text import through the production CLI entry point.</summary>
[TestFixture]
[Category("CliIntegration")]
[NonParallelizable]
public sealed class NotebookImportIntegrationTests
{
    /// <summary>Verifies help documents the source, target, conflict, and dry-run options.</summary>
    [Test]
    public async Task Help_DescribesImportContract()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("notebooks", "import", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks import"));
            Assert.That(result.StandardOutput, Does.Contain("<directory>"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--on-conflict <policy>"));
            Assert.That(result.StandardOutput, Does.Contain("--dry-run"));
            Assert.That(result.StandardOutput, Does.Contain("strict-UTF-8"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies selected-notebook import is flat and preserves decoded UTF-8 text.</summary>
    [Test]
    public async Task Import_SelectedNotebook_CreatesRootLowercaseFilesAndSummary()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "selected");
        var source = CreateSourceDirectory(harness, "selected-import");
        const string expected = "Café\r\n日本語\n";
        await File.WriteAllBytesAsync(
            Path.Combine(source, "Plan%2F2026~mn~2~.txt"),
            Encoding.UTF8.Preamble.ToArray()
                .Concat(Encoding.UTF8.GetBytes(expected))
                .ToArray());
        await File.WriteAllTextAsync(Path.Combine(source, "Ignored.TXT"), "uppercase");
        var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "Ignored.txt"), "nested");

        var result = await harness.RunAsync("notebooks", "import", source);

        var repository = CreatePageRepository();
        var imported = await repository.FindPageAsync(
            notebookPath,
            "Plan/2026",
            1,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.StartWith("Import completed: "));
            Assert.That(result.StandardOutput, Does.Contain($"source=\"{source}\""));
            Assert.That(result.StandardOutput, Does.Contain($"target=\"{notebookPath}\""));
            Assert.That(
                result.StandardOutput,
                Does.Match(@"created=1, replaced=0, skipped=0\.\r?\n$"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(imported, Is.Not.Null);
            Assert.That(imported!.Text, Is.EqualTo(expected));
            Assert.That(
                await repository.FindPageAsync(
                    notebookPath,
                    "Ignored",
                    1,
                    CancellationToken.None),
                Is.Null);
        }
    }

    /// <summary>Verifies dry-run, skip, and replace share stable counts and mutation rules.</summary>
    [Test]
    public async Task Import_ConflictPolicies_ApplyExplicitBehavior()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "policies");
        var repository = CreatePageRepository();
        var existing = await repository.CreatePageAsync(
            notebookPath,
            "Existing",
            "original",
            "saved/path",
            CancellationToken.None);
        var source = CreateSourceDirectory(harness, "policy-import");
        await File.WriteAllTextAsync(Path.Combine(source, "Existing.txt"), "replacement");
        await File.WriteAllTextAsync(Path.Combine(source, "New.txt"), "created");

        var failed = await harness.RunAsync("notebooks", "import", source);
        var dryRun = await harness.RunAsync(
            "notebooks",
            "import",
            source,
            "--on-conflict",
            "replace",
            "--dry-run");
        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(source, "Existing.txt")),
            Is.EqualTo("replacement"));
        Assert.That(
            await File.ReadAllTextAsync(Path.Combine(source, "New.txt")),
            Is.EqualTo("created"));
        var skipped = await harness.RunAsync(
            "notebooks",
            "import",
            source,
            "--on-conflict",
            "skip");
        File.Delete(Path.Combine(source, "New.txt"));
        var replacedResult = await harness.RunAsync(
            "notebooks",
            "import",
            source,
            "--on-conflict",
            "replace");

        var replaced = await repository.FindPageAsync(
            notebookPath,
            existing.Guid,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(failed.StandardOutput, Is.Empty);
            Assert.That(failed.StandardError, Does.Contain("already contains"));
            Assert.That(dryRun.ExitCode, Is.Zero, dryRun.StandardError);
            Assert.That(dryRun.StandardOutput, Does.StartWith("Import validated: "));
            Assert.That(dryRun.StandardOutput, Does.Contain("created=1, replaced=1, skipped=0"));
            Assert.That(skipped.ExitCode, Is.Zero, skipped.StandardError);
            Assert.That(skipped.StandardOutput, Does.Contain("created=1, replaced=0, skipped=1"));
            Assert.That(replacedResult.ExitCode, Is.Zero, replacedResult.StandardError);
            Assert.That(replacedResult.StandardOutput, Does.Contain("created=0, replaced=1, skipped=0"));
            Assert.That(replaced, Is.Not.Null);
            Assert.That(replaced!.Guid, Is.EqualTo(existing.Guid));
            Assert.That(replaced.Name, Is.EqualTo(existing.Name));
            Assert.That(replaced.TagDict, Is.EqualTo(existing.TagDict));
            Assert.That(replaced.CreateTime, Is.EqualTo(existing.CreateTime));
            Assert.That(replaced.Text, Is.EqualTo("replacement"));
            Assert.That(
                await repository.FindPageAsync(
                    notebookPath,
                    "New",
                    1,
                    CancellationToken.None),
                Is.Not.Null);
        }
    }

    /// <summary>Verifies invalid UTF-8 fails without importing earlier files.</summary>
    [Test]
    public async Task Import_InvalidUtf8_ReturnsValidationWithoutPartialWrites()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "encoding");
        var source = CreateSourceDirectory(harness, "encoding-import");
        await File.WriteAllTextAsync(Path.Combine(source, "Before.txt"), "valid");
        await File.WriteAllBytesAsync(
            Path.Combine(source, "Invalid.txt"),
            new byte[] { 0x66, 0x80, 0x67 });

        var result = await harness.RunAsync("notebooks", "import", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("valid UTF-8"));
            Assert.That(
                await CreatePageRepository().CountPagesAsync(
                    notebookPath,
                    CancellationToken.None),
                Is.Zero);
        }
    }

    /// <summary>Verifies a late fail-policy conflict rolls back all planned creates.</summary>
    [Test]
    public async Task Import_FailPolicyConflictAfterNewItem_CommitsNothing()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "atomic-fail");
        var repository = CreatePageRepository();
        var existing = await repository.CreatePageAsync(
            notebookPath,
            "Z existing",
            "original",
            null!,
            CancellationToken.None);
        var source = CreateSourceDirectory(harness, "atomic-fail-import");
        await File.WriteAllTextAsync(Path.Combine(source, "A new.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "Z existing.txt"), "replacement");

        var result = await harness.RunAsync("notebooks", "import", source);

        var retained = await repository.FindPageAsync(
            notebookPath,
            existing.Guid,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("already contains"));
            Assert.That(retained?.Text, Is.EqualTo("original"));
            Assert.That(
                await repository.FindPageAsync(
                    notebookPath,
                    "A new",
                    1,
                    CancellationToken.None),
                Is.Null);
        }
    }

    /// <summary>Verifies a late replace ambiguity rolls back all planned creates.</summary>
    [Test]
    public async Task Import_ReplaceAmbiguityAfterNewItem_CommitsNothing()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "atomic-replace");
        var repository = CreatePageRepository();
        var first = await repository.CreatePageAsync(
            notebookPath,
            "Z duplicate",
            "first",
            null!,
            CancellationToken.None);
        var second = await repository.CreatePageAsync(
            notebookPath,
            "Z duplicate",
            "second",
            null!,
            CancellationToken.None);
        var source = CreateSourceDirectory(harness, "atomic-replace-import");
        await File.WriteAllTextAsync(Path.Combine(source, "A new.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "Z duplicate.txt"), "replacement");

        var result = await harness.RunAsync(
            "notebooks",
            "import",
            source,
            "--on-conflict",
            "replace");

        var retainedFirst = await repository.FindPageAsync(
            notebookPath,
            first.Guid,
            CancellationToken.None);
        var retainedSecond = await repository.FindPageAsync(
            notebookPath,
            second.Guid,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("exactly one existing page"));
            Assert.That(retainedFirst?.Text, Is.EqualTo("first"));
            Assert.That(retainedSecond?.Text, Is.EqualTo("second"));
            Assert.That(
                await repository.FindPageAsync(
                    notebookPath,
                    "A new",
                    1,
                    CancellationToken.None),
                Is.Null);
        }
    }

    /// <summary>Verifies v1 and v2 encodings of one name are rejected before writes.</summary>
    [Test]
    public async Task Import_DuplicateDecodedName_ReturnsConflictWithoutWrites()
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "duplicate");
        var source = CreateSourceDirectory(harness, "duplicate-import");
        await File.WriteAllTextAsync(Path.Combine(source, "~mn~1~A%2FB.txt"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(source, "A%2FB~mn~2~.txt"), "current");

        var result = await harness.RunAsync("notebooks", "import", source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("same page name"));
            Assert.That(
                await CreatePageRepository().CountPagesAsync(
                    notebookPath,
                    CancellationToken.None),
                Is.Zero);
        }
    }

    /// <summary>Verifies read-only metadata is enforced and mapped to conflict.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task Import_ReadOnlyNotebook_ReturnsConflictWithoutWrites(bool dryRun)
    {
        using var harness = new CliInProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "read-only");
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        await new SqliteNotebookMetadataRepository(factory).UpdateAsync(
            notebookPath,
            new NotebookMetadataPatch().SetReadOnly(true),
            CancellationToken.None);
        var source = CreateSourceDirectory(harness, "read-only-import");
        await File.WriteAllTextAsync(Path.Combine(source, "Entry.txt"), "text");

        var arguments = new List<string> { "notebooks", "import", source };
        if (dryRun)
            arguments.Add("--dry-run");

        var result = await harness.RunAsync(arguments.ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("read-only"));
            Assert.That(
                await CreatePageRepository().CountPagesAsync(
                    notebookPath,
                    CancellationToken.None),
                Is.Zero);
        }
    }

    /// <summary>Verifies an invalid policy fails before resolving a current notebook.</summary>
    [Test]
    public async Task Import_InvalidConflictPolicy_ReturnsValidationBeforeTargetResolution()
    {
        using var harness = new CliInProcessHarness();
        var source = CreateSourceDirectory(harness, "invalid-policy-import");

        var result = await harness.RunAsync(
            "notebooks",
            "import",
            source,
            "--on-conflict",
            "merge");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("fail, skip, or replace"));
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

    /// <summary>Verifies a missing import directory maps to not-found.</summary>
    [Test]
    public async Task Import_MissingDirectory_ReturnsNotFound()
    {
        using var harness = new CliInProcessHarness();
        var missingDirectory = Path.Combine(
            harness.WorkingDirectory,
            "missing-import-directory");

        var result = await harness.RunAsync(
            "notebooks",
            "import",
            missingDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(
                result.StandardError,
                Does.Contain("import directory does not exist"));
        }
    }

    /// <summary>Verifies a missing import directory emits a parser diagnostic.</summary>
    [Test]
    public async Task Import_WithoutDirectory_WritesDiagnosticAndReturnsFailure()
    {
        using var harness = new CliInProcessHarness();

        var result = await harness.RunAsync("notebooks", "import");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("import directory"));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
        }
    }

    static async Task<string> CreateAndSelectNotebookAsync(
        CliInProcessHarness harness,
        string name)
    {
        var create = await harness.RunAsync("create", name);
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var use = await harness.RunAsync("use", name);
        Assert.That(use.ExitCode, Is.Zero, use.StandardError);
        return Path.Combine(harness.WorkingDirectory, name + ".mnote");
    }

    static string CreateSourceDirectory(CliInProcessHarness harness, string name)
    {
        var path = Path.Combine(harness.TemporaryDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    static SqlitePageRepository CreatePageRepository()
    {
        return new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
