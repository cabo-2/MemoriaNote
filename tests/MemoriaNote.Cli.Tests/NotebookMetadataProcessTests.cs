using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook metadata workflows through the CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookMetadataProcessTests
{
    static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Verifies help documents every editable field and standard input.</summary>
    [Test]
    public async Task Help_DescribesMetadataContract()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("notebooks", "metadata", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain("Usage: mn notebooks metadata"));
            Assert.That(result.StandardOutput, Does.Contain("--notebook <notebook>"));
            Assert.That(result.StandardOutput, Does.Contain("--name <value>"));
            Assert.That(result.StandardOutput, Does.Contain("--title <value>"));
            Assert.That(result.StandardOutput, Does.Contain("--description <value>"));
            Assert.That(result.StandardOutput, Does.Contain("--author <value>"));
            Assert.That(result.StandardOutput, Does.Contain("--tag <value>"));
            Assert.That(result.StandardOutput, Does.Contain("--read-only <value>"));
            Assert.That(result.StandardOutput, Does.Contain("standard input"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies selected metadata is displayed with stable escaping.</summary>
    [Test]
    public async Task Metadata_SelectedNotebook_DisplaysKnownFields()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "selected");
        var repository = CreateMetadataRepository();
        await repository.UpdateAsync(
            notebookPath,
            new NotebookMetadataPatch()
                .SetDescription("first\nsecond")
                .SetAuthor("Author\\Name")
                .SetTag("planning")
                .SetCreateTime(new DateTime(2026, 1, 2, 3, 4, 5)),
            CancellationToken.None);

        var result = await harness.RunAsync("notebooks", "metadata");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Notebook: selected.mnote"));
            Assert.That(result.StandardOutput, Does.Contain("Name: selected"));
            Assert.That(result.StandardOutput, Does.Contain("Title: selected"));
            Assert.That(result.StandardOutput, Does.Contain("Description: first\\nsecond"));
            Assert.That(result.StandardOutput, Does.Contain("Author: Author\\\\Name"));
            Assert.That(result.StandardOutput, Does.Contain("Tag: planning"));
            Assert.That(result.StandardOutput, Does.Contain("ReadOnly: false"));
            Assert.That(result.StandardOutput, Does.Contain("Version: 1"));
            Assert.That(result.StandardOutput, Does.Contain("CreateTime: 2026-01-02T03:04:05"));
            Assert.That(result.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies display names may duplicate without changing file selection.</summary>
    [Test]
    public async Task Metadata_DuplicateDisplayNames_AreAllowedWithoutChangingCurrent()
    {
        using var harness = new CliProcessHarness();
        var currentPath = await CreateAndSelectNotebookAsync(harness, "current");
        var create = await harness.RunAsync("create", "explicit");
        Assert.That(create.ExitCode, Is.Zero, create.StandardError);
        var explicitPath = Path.Combine(harness.WorkingDirectory, "explicit.mnote");

        var first = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--name",
            "shared-name");
        var second = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--notebook",
            "explicit",
            "--name",
            "shared-name");
        var status = await harness.RunAsync("status");
        var repository = CreateMetadataRepository();
        var current = await repository.LoadAsync(currentPath, CancellationToken.None);
        var explicitNotebook = await repository.LoadAsync(explicitPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero, first.StandardError);
            Assert.That(second.ExitCode, Is.Zero, second.StandardError);
            Assert.That(second.StandardOutput, Does.Contain("notebook=explicit.mnote"));
            Assert.That(current.Metadata.Name, Is.EqualTo("shared-name"));
            Assert.That(explicitNotebook.Metadata.Name, Is.EqualTo("shared-name"));
            Assert.That(File.Exists(currentPath), Is.True);
            Assert.That(File.Exists(explicitPath), Is.True);
            Assert.That(status.StandardOutput, Does.Contain("current.mnote"));
        }
    }

    /// <summary>Verifies stdin updates preserve content except one terminal line ending.</summary>
    [Test]
    public async Task Metadata_DescriptionFromStandardInput_PreservesMultilineValue()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "stdin");
        const string input = "first\r\nsecond\tvalue\n";

        var result = await harness.RunAsync(
            new[]
            {
                "notebooks",
                "metadata",
                "--description",
                "-"
            },
            input,
            ProcessTimeout);
        var loaded = await CreateMetadataRepository().LoadAsync(
            notebookPath,
            CancellationToken.None);
        var displayed = await harness.RunAsync("notebooks", "metadata");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("field=description"));
            Assert.That(loaded.Metadata.Description, Is.EqualTo("first\r\nsecond\tvalue"));
            Assert.That(
                displayed.StandardOutput,
                Does.Contain("Description: first\\r\\nsecond\\tvalue"));
        }
    }

    /// <summary>Verifies multiple update options fail without modifying metadata.</summary>
    [Test]
    public async Task Metadata_MultipleUpdates_ReturnValidationWithoutWrites()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "multiple");

        var result = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--name",
            "changed-name",
            "--title",
            "Changed title");
        var loaded = await CreateMetadataRepository().LoadAsync(
            notebookPath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("only one metadata update option"));
            Assert.That(loaded.Metadata.Name, Is.EqualTo("multiple"));
            Assert.That(loaded.Metadata.Title, Is.EqualTo("multiple"));
        }
    }

    /// <summary>Verifies an explicit empty optional value is persisted.</summary>
    [Test]
    public async Task Metadata_EmptyOptionalValue_IsStoredAsEmptyString()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "empty");
        var repository = CreateMetadataRepository();
        await repository.UpdateAsync(
            notebookPath,
            new NotebookMetadataPatch().SetAuthor("existing"),
            CancellationToken.None);

        var result = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--author",
            string.Empty);
        var loaded = await repository.LoadAsync(notebookPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("field=author"));
            Assert.That(loaded.Metadata.Author, Is.EqualTo(string.Empty));
        }
    }

    /// <summary>Verifies field-specific controls reject names but allow descriptions.</summary>
    [Test]
    public async Task Metadata_ControlCharacters_ApplyFieldSpecificRules()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "controls");

        var rejected = await harness.RunAsync(
            new[] { "notebooks", "metadata", "--name", "-" },
            "invalid\nname",
            ProcessTimeout);
        var accepted = await harness.RunAsync(
            new[] { "notebooks", "metadata", "--description", "-" },
            "valid\ntext",
            ProcessTimeout);
        var loaded = await CreateMetadataRepository().LoadAsync(
            notebookPath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(rejected.StandardError, Does.Contain("control character"));
            Assert.That(accepted.ExitCode, Is.Zero, accepted.StandardError);
            Assert.That(loaded.Metadata.Name, Is.EqualTo("controls"));
            Assert.That(loaded.Metadata.Description, Is.EqualTo("valid\ntext"));
        }
    }

    /// <summary>Verifies read-only can be enabled, observed as no-op, and disabled.</summary>
    [Test]
    public async Task Metadata_ReadOnly_CanBeToggledAndReportsNoOp()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "access");

        var enabled = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--read-only",
            "true");
        var unchanged = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--read-only",
            "true");
        var disabled = await harness.RunAsync(
            "notebooks",
            "metadata",
            "--read-only",
            "false");
        var loaded = await CreateMetadataRepository().LoadAsync(
            notebookPath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabled.ExitCode, Is.Zero, enabled.StandardError);
            Assert.That(enabled.StandardOutput, Does.Contain("field=read-only"));
            Assert.That(unchanged.ExitCode, Is.Zero, unchanged.StandardError);
            Assert.That(unchanged.StandardOutput, Does.Contain("Metadata unchanged"));
            Assert.That(disabled.ExitCode, Is.Zero, disabled.StandardError);
            Assert.That(loaded.Metadata.ReadOnly, Is.False);
        }
    }

    /// <summary>Verifies the legacy editor is a non-mutating migration stub.</summary>
    [Test]
    public async Task LegacyWorkEdit_ReturnsGuidanceWithoutWrites()
    {
        using var harness = new CliProcessHarness();
        var notebookPath = await CreateAndSelectNotebookAsync(harness, "legacy");

        var result = await harness.RunAsync("work", "edit");
        var loaded = await CreateMetadataRepository().LoadAsync(
            notebookPath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("no longer supported"));
            Assert.That(result.StandardError, Does.Contain("mn notebooks metadata"));
            Assert.That(loaded.Metadata.Name, Is.EqualTo("legacy"));
            Assert.That(loaded.Metadata.Title, Is.EqualTo("legacy"));
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

    static SqliteNotebookMetadataRepository CreateMetadataRepository()
    {
        return new SqliteNotebookMetadataRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }
}
