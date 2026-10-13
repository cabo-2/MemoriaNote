using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using MemoriaNote.Cli.UserConfig;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies values inherited by the CLI process.</summary>
[TestFixture]
[Category("Process")]
public sealed class ProcessInheritanceTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>
    /// Verifies the inherited directory and a Unicode argument reach the CLI unchanged.
    /// </summary>
    [Test]
    public async Task Create_InheritsCurrentDirectoryAndUnicodeArgument()
    {
        using var harness = new CliProcessHarness();
        const string notebookName = "共有 ノート";
        var notebookPath = Path.Combine(
            harness.WorkingDirectory,
            notebookName + ".mnote");

        var result = await harness.RunAsync("create", notebookName);

        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var metadataRepository = new SqliteNotebookMetadataRepository(factory);
        var metadata = await metadataRepository.LoadAsync(
            notebookPath,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Does.Contain(notebookPath));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(File.Exists(notebookPath), Is.True);
            Assert.That(metadata.HasIssues, Is.False);
            Assert.That(metadata.Metadata.Name, Is.EqualTo(notebookName));
            Assert.That(metadata.Metadata.Title, Is.EqualTo(notebookName));
            Assert.That(
                metadata.Metadata.Version,
                Is.EqualTo(NotebookDbContext.CurrentVersion));
            Assert.That(File.Exists(harness.ConfigurationPath), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    harness.WorkingDirectory,
                    WorkspaceConfigurationStore.FileName)),
                Is.False);
        }
    }

    /// <summary>Verifies the child editor inherits the configured environment variable.</summary>
    [Test]
    public async Task EnvironmentConfiguration_New_InheritsEditorEnvironment()
    {
        using var harness = new CliProcessHarness();
        const string variable = "MEMORIA_NOTE_CONFIGURED_EDITOR";
        await WriteConfigurationAsync(
            harness,
            UserEditorConfiguration.FromEnvironment(variable, new[] { "--wait" }));
        harness.SetEnvironmentVariable(variable, harness.TestEditorExecutablePath);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Environment body");
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync("new", "Environment");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That((await FindPageAsync(harness, "Environment"))?.Text,
                Is.EqualTo("Environment body"));
        }
    }

    static async Task WriteConfigurationAsync(
        CliProcessHarness harness,
        UserEditorConfiguration editor)
    {
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        await File.WriteAllTextAsync(
            harness.UserConfigurationPath,
            UserConfigurationCodec.Serialize(new UserConfigurationModel(editor)),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static async Task<Page?> FindPageAsync(CliProcessHarness harness, string name)
    {
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        return (await repository.ListPagesByHeadingAsync(
            Path.Combine(harness.WorkingDirectory, "work.mnote"),
            name,
            CancellationToken.None)).SingleOrDefault();
    }

    static void AssertSucceeded(CliProcessResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardError, Is.Empty);
        }
    }
}
