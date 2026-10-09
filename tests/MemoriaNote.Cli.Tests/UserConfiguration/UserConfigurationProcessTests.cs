using System.Text;
using MemoriaNote.Cli.Tests.Infrastructure;
using MemoriaNote.Cli.UserConfig;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies the activated user configuration through the process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class UserConfigurationProcessTests
{
    const string EditedTextEnvironmentVariable = "MEMORIA_NOTE_TEST_EDITOR_TEXT";

    /// <summary>Verifies the public config command tree and removal of config edit.</summary>
    [Test]
    public async Task Help_DescribesPublicConfigCommands()
    {
        using var harness = new CliProcessHarness();

        var root = await harness.RunAsync("--help");
        var config = await harness.RunAsync("config", "--help");
        var editor = await harness.RunAsync("config", "editor", "--help");
        var setup = await harness.RunAsync("config", "editor", "setup");
        var removedEdit = await harness.RunAsync("config", "edit", "--help");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.ExitCode, Is.Zero);
            Assert.That(ContainsCommand(root.StandardOutput, "config"), Is.True);
            Assert.That(ContainsCommand(config.StandardOutput, "path"), Is.True);
            Assert.That(ContainsCommand(config.StandardOutput, "show"), Is.True);
            Assert.That(ContainsCommand(config.StandardOutput, "validate"), Is.True);
            Assert.That(ContainsCommand(config.StandardOutput, "editor"), Is.True);
            Assert.That(ContainsCommand(config.StandardOutput, "edit"), Is.False);
            Assert.That(ContainsCommand(editor.StandardOutput, "setup"), Is.True);
            Assert.That(ContainsCommand(editor.StandardOutput, "show"), Is.True);
            Assert.That(ContainsCommand(editor.StandardOutput, "unset"), Is.True);
            Assert.That(setup.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(setup.StandardError, Does.Contain("requires interactive input"));
            Assert.That(removedEdit.ExitCode, Is.EqualTo(1));
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
            Assert.That(File.Exists(harness.LegacyConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies config inspection never changes user, workspace, database, or archive bytes.</summary>
    [Test]
    public async Task ReadOnlyCommands_PreserveAllPersistedState()
    {
        using var harness = new CliProcessHarness();
        await WriteConfigurationAsync(
            harness,
            UserEditorConfiguration.FromProgram("code", new[] { "--wait" }));
        var configurationBytes = await File.ReadAllBytesAsync(
            harness.UserConfigurationPath);
        var legacyBytes = new byte[] { 0xff, 0x00, 0x41, 0x0a };
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);
        var workspacePath = Path.Combine(
            harness.WorkingDirectory,
            WorkspaceConfigurationStore.FileName);
        var workspaceBytes = Encoding.UTF8.GetBytes(
            "format_version = 1\ncurrent_notebook = \"work.mnote\"\n");
        await File.WriteAllBytesAsync(workspacePath, workspaceBytes);
        var databasePath = Path.Combine(harness.WorkingDirectory, "work.mnote");
        var databaseBytes = new byte[] { 1, 3, 3, 7 };
        await File.WriteAllBytesAsync(databasePath, databaseBytes);
        var archivePath = Path.Combine(harness.WorkingDirectory, "backup.zip");
        var archiveBytes = new byte[] { 0x50, 0x4b, 0x03, 0x04 };
        await File.WriteAllBytesAsync(archivePath, archiveBytes);

        var path = await harness.RunAsync("config", "path");
        var show = await harness.RunAsync("config", "show");
        var validate = await harness.RunAsync("config", "validate");
        var editorShow = await harness.RunAsync("config", "editor", "show");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(path.ExitCode, Is.Zero);
            Assert.That(path.StandardOutput, Does.Contain("Status: present"));
            Assert.That(show.ExitCode, Is.Zero);
            Assert.That(show.StandardOutput, Does.Contain("Status: valid"));
            Assert.That(validate.ExitCode, Is.Zero);
            Assert.That(validate.StandardOutput, Does.Contain("Status: valid"));
            Assert.That(editorShow.ExitCode, Is.Zero);
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(configurationBytes));
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
            Assert.That(await File.ReadAllBytesAsync(workspacePath), Is.EqualTo(workspaceBytes));
            Assert.That(await File.ReadAllBytesAsync(databasePath), Is.EqualTo(databaseBytes));
            Assert.That(await File.ReadAllBytesAsync(archivePath), Is.EqualTo(archiveBytes));
            Assert.That(
                Directory.GetFiles(
                    harness.ApplicationDataDirectory,
                    "configuration.json.corrupt-*"),
                Is.Empty);
        }
    }

    /// <summary>Verifies read-only commands do not create a missing configuration.</summary>
    [Test]
    public async Task ReadOnlyCommands_MissingConfiguration_RemainNonDestructive()
    {
        using var harness = new CliProcessHarness();

        var path = await harness.RunAsync("config", "path");
        var show = await harness.RunAsync("config", "show");
        var validate = await harness.RunAsync("config", "validate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(path.ExitCode, Is.Zero);
            Assert.That(path.StandardOutput, Does.Contain("Status: missing"));
            Assert.That(show.ExitCode, Is.Zero);
            Assert.That(show.StandardOutput, Does.Contain("Status: missing"));
            Assert.That(validate.ExitCode, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
            Assert.That(Directory.Exists(harness.ApplicationDataDirectory), Is.False);
        }
    }

    /// <summary>Verifies unset creates only the new TOML and is a byte-preserving no-op later.</summary>
    [Test]
    public async Task EditorUnset_CreatesNewConfigurationAndThenNoOps()
    {
        using var harness = new CliProcessHarness();
        var legacyBytes = Encoding.UTF8.GetBytes("legacy configuration");
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);

        var first = await harness.RunAsync("config", "editor", "unset");
        var savedBytes = await File.ReadAllBytesAsync(harness.UserConfigurationPath);
        var second = await harness.RunAsync("config", "editor", "unset");
        var show = await harness.RunAsync("config", "editor", "show");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.Zero);
            Assert.That(first.StandardOutput, Does.Contain("was unset"));
            Assert.That(second.ExitCode, Is.Zero);
            Assert.That(second.StandardOutput, Does.Contain("already unset"));
            Assert.That(show.StandardOutput, Does.Contain("Editor selection: unset"));
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(savedBytes));
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
        }
    }

    /// <summary>Verifies a configured program and placeholder drive both new and edit.</summary>
    [Test]
    public async Task ProgramConfiguration_NewAndEdit_UsesArgumentsAndPlaceholder()
    {
        using var harness = new CliProcessHarness();
        await WriteConfigurationAsync(
            harness,
            UserEditorConfiguration.FromProgram(
                harness.TestEditorExecutablePath,
                new[] { "--wait", "{file}" }));
        var configurationBytes = await File.ReadAllBytesAsync(
            harness.UserConfigurationPath);
        var legacyBytes = Encoding.UTF8.GetBytes("ignored legacy bytes");
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Created body");
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var created = await harness.RunAsync("new", "Roadmap");
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Edited body");
        var edited = await harness.RunAsync("edit", "Roadmap");

        var page = await FindPageAsync(harness, "Roadmap");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.ExitCode, Is.Zero);
            Assert.That(edited.ExitCode, Is.Zero);
            Assert.That(page?.Text, Is.EqualTo("Edited body"));
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(configurationBytes));
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
        }
    }

    /// <summary>Verifies environment selection preserves arguments and appends the file path.</summary>
    [Test]
    public async Task EnvironmentConfiguration_New_UsesArgumentsAndAppendedFile()
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

    /// <summary>Verifies an invocation override never loads or modifies invalid TOML.</summary>
    [Test]
    public async Task InvocationOverride_InvalidToml_BypassesAllConfigurationAccess()
    {
        using var harness = new CliProcessHarness();
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        var invalidToml = Encoding.UTF8.GetBytes("{ invalid TOML\n");
        var legacyBytes = new byte[] { 0xfe, 0xed, 0xfa, 0xce };
        await File.WriteAllBytesAsync(harness.UserConfigurationPath, invalidToml);
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);
        harness.SetEnvironmentVariable(EditedTextEnvironmentVariable, "Override body");
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync(
            "new",
            "Override",
            "--editor",
            harness.TestEditorExecutablePath,
            "--editor-arg=--wait",
            "--editor-arg={file}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That((await FindPageAsync(harness, "Override"))?.Text,
                Is.EqualTo("Override body"));
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(invalidToml));
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
        }
    }

    /// <summary>Verifies missing configuration ignores legacy JSON and reports setup guidance.</summary>
    [Test]
    public async Task New_MissingConfiguration_IgnoresLegacyAndChangesNoPage()
    {
        using var harness = new CliProcessHarness();
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        var legacyBytes = Encoding.UTF8.GetBytes("legacy editor configuration");
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync("new", "Missing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(result.StandardError, Does.Contain("External editor is not configured"));
            Assert.That(result.StandardError, Does.Contain("mn config editor setup"));
            Assert.That(await FindPageAsync(harness, "Missing"), Is.Null);
            Assert.That(File.Exists(harness.UserConfigurationPath), Is.False);
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
        }
    }

    /// <summary>Verifies explicit unset reports setup guidance without changing TOML or a page.</summary>
    [Test]
    public async Task Edit_UnsetConfiguration_ChangesNoPersistedState()
    {
        using var harness = new CliProcessHarness();
        await WriteConfigurationAsync(harness, UserEditorConfiguration.Unset);
        var configurationBytes = await File.ReadAllBytesAsync(
            harness.UserConfigurationPath);
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));
        await CreatePageDirectlyAsync(harness, "Existing", "Before");

        var result = await harness.RunAsync("edit", "Existing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(result.StandardError, Does.Contain("mn config editor setup"));
            Assert.That((await FindPageAsync(harness, "Existing"))?.Text, Is.EqualTo("Before"));
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(configurationBytes));
        }
    }

    /// <summary>Verifies invalid and unsupported TOML never fall back to legacy JSON.</summary>
    [TestCase("{ invalid TOML\n", "invalid")]
    [TestCase(
        "format_version = 2\n\n[editor]\nselection = \"unset\"\n",
        "not supported")]
    public async Task New_InvalidOrUnsupportedToml_DoesNotFallback(
        string toml,
        string expectedDiagnostic)
    {
        using var harness = new CliProcessHarness();
        Directory.CreateDirectory(harness.ApplicationDataDirectory);
        var tomlBytes = Encoding.UTF8.GetBytes(toml);
        var legacyBytes = Encoding.UTF8.GetBytes("legacy configuration stays untouched");
        await File.WriteAllBytesAsync(harness.UserConfigurationPath, tomlBytes);
        await File.WriteAllBytesAsync(harness.LegacyConfigurationPath, legacyBytes);
        harness.SetEnvironmentVariable("EDITOR", harness.TestEditorExecutablePath);
        AssertSucceeded(await harness.RunAsync("create", "work"));
        AssertSucceeded(await harness.RunAsync("use", "work"));

        var result = await harness.RunAsync("new", "Rejected");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(result.StandardError, Does.Contain(expectedDiagnostic).IgnoreCase);
            Assert.That(await FindPageAsync(harness, "Rejected"), Is.Null);
            Assert.That(
                await File.ReadAllBytesAsync(harness.UserConfigurationPath),
                Is.EqualTo(tomlBytes));
            Assert.That(
                await File.ReadAllBytesAsync(harness.LegacyConfigurationPath),
                Is.EqualTo(legacyBytes));
            Assert.That(
                Directory.GetFiles(
                    harness.ApplicationDataDirectory,
                    "configuration.json.corrupt-*"),
                Is.Empty);
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

    static async Task CreatePageDirectlyAsync(
        CliProcessHarness harness,
        string name,
        string text)
    {
        var repository = new SqlitePageRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
        await repository.CreatePageAsync(
            Path.Combine(harness.WorkingDirectory, "work.mnote"),
            name,
            text,
            string.Empty,
            CancellationToken.None);
    }

    static bool ContainsCommand(string help, string command)
    {
        return help.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart())
            .Any(line => line.StartsWith(command + " ", StringComparison.Ordinal));
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
