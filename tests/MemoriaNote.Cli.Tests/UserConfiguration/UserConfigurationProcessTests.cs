using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using MemoriaNote.Cli.UserConfig;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies process-specific user configuration boundaries.</summary>
[TestFixture]
[Category("Process")]
public sealed class UserConfigurationProcessTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>Verifies redirected input cannot enter interactive editor setup.</summary>
    [Test]
    public async Task EditorSetup_RedirectedInput_ReturnsValidationFailure()
    {
        using var harness = new CliProcessHarness();

        var result = await harness.RunAsync("config", "editor", "setup");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardOutput, Is.Empty);
            Assert.That(result.StandardError, Does.Contain("requires interactive input"));
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
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
