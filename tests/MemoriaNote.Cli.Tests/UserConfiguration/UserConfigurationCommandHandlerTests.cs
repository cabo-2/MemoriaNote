using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.UserConfig;
using MemoriaNote.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies the dormant user configuration command handlers.</summary>
[TestFixture]
public sealed class UserConfigurationCommandHandlerTests
{
    /// <summary>Verifies that path reports existence without loading or creating configuration.</summary>
    [Test]
    public async Task Path_ReportsMissingAndPresentWithoutLoadingStore()
    {
        using var fixture = HandlerFixture.Create(UserConfigurationLoadResult.Missing);

        var missing = await fixture.Path.ExecuteAsync(CancellationToken.None);
        Directory.CreateDirectory(fixture.ApplicationDataDirectory);
        await File.WriteAllTextAsync(fixture.ConfigurationPath, "not parsed");
        var present = await fixture.Path.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing, Is.Zero);
            Assert.That(present, Is.Zero);
            Assert.That(fixture.Store.LoadCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Status: missing" + Environment.NewLine));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Status: present" + Environment.NewLine));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies that show distinguishes a missing file without saving defaults.</summary>
    [Test]
    public async Task Show_MissingConfiguration_IsReadOnlyAndExplicit()
    {
        using var fixture = HandlerFixture.Create(UserConfigurationLoadResult.Missing);

        var result = await fixture.Show.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Store.LoadCount, Is.EqualTo(1));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Status: missing"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor selection: (not configured)"));
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Editor source: none"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Effective executable: (not configured)"));
        }
    }

    /// <summary>Verifies that show includes persisted and effective environment editor values.</summary>
    [Test]
    public async Task Show_EnvironmentConfiguration_ReportsPersistedAndEffectiveValues()
    {
        var loaded = Loaded(UserEditorConfiguration.FromEnvironment(
            "VISUAL",
            new[] { "--wait", "{file}" }));
        using var fixture = HandlerFixture.Create(
            loaded,
            environmentName: "VISUAL",
            environmentValue: "code");

        var result = await fixture.Show.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Status: valid"));
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Format version: 1"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor selection: environment"));
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Editor variable: VISUAL"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor arguments: [\"--wait\",\"{file}\"]"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor source: environment variable VISUAL"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Effective executable: code"));
        }
    }

    /// <summary>Verifies that validate reports a valid loaded format without saving.</summary>
    [Test]
    public async Task Validate_LoadedConfiguration_ReportsVersionWithoutSaving()
    {
        using var fixture = HandlerFixture.Create(Loaded(
            UserEditorConfiguration.FromProgram("nano")));

        var result = await fixture.Validate.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Is.EqualTo(
                    "Status: valid" + Environment.NewLine +
                    "Format version: 1" + Environment.NewLine));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that validate distinguishes a missing file with the not-found exit code.</summary>
    [Test]
    public async Task Validate_MissingConfiguration_ReturnsNotFoundWithoutSaving()
    {
        using var fixture = HandlerFixture.Create(UserConfigurationLoadResult.Missing);

        var result = await fixture.Validate.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.NotFound));
            Assert.That(
                fixture.Output.StandardOutput,
                Is.EqualTo("Status: missing" + Environment.NewLine));
            Assert.That(
                fixture.Output.StandardError,
                Is.EqualTo(
                    "Error: User configuration does not exist." + Environment.NewLine));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies that invalid and unsupported content retain validation failures.</summary>
    [TestCase(false, "invalid user configuration")]
    [TestCase(true, "not supported")]
    public async Task Show_InvalidConfiguration_ReturnsValidation(
        bool unsupported,
        string expectedMessage)
    {
        using var fixture = HandlerFixture.Create(UserConfigurationLoadResult.Missing);
        fixture.Store.LoadException = unsupported
            ? new UnsupportedUserConfigurationVersionException(2)
            : new UserConfigurationFormatException("invalid user configuration");

        var result = await fixture.Show.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain(unsupported ? "Status: unsupported" : "Status: invalid"));
            Assert.That(fixture.Output.StandardError, Does.Contain(expectedMessage));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies editor show displays explicit unset instead of treating it as missing.</summary>
    [Test]
    public async Task EditorShow_UnsetConfiguration_RemainsDistinctFromMissing()
    {
        using var fixture = HandlerFixture.Create(Loaded(UserEditorConfiguration.Unset));

        var result = await fixture.EditorShow.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor selection: unset"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor source: user configuration"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Effective executable: (not configured)"));
        }
    }

    /// <summary>Verifies setup saves a program and exact argument values after confirmation.</summary>
    [Test]
    public async Task EditorSetup_ProgramSelection_SavesAfterConfirmation()
    {
        var loaded = Loaded(UserEditorConfiguration.Unset);
        using var fixture = HandlerFixture.Create(
            loaded,
            input: new[] { "2", "code", "3", "--wait", "", "{file}", "yes" });

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Store.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.Store.ExpectedRevision, Is.SameAs(loaded.Revision));
            Assert.That(
                fixture.Store.SavedConfiguration!.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Program));
            Assert.That(fixture.Store.SavedConfiguration.Editor.Executable, Is.EqualTo("code"));
            Assert.That(
                fixture.Store.SavedConfiguration.Editor.Arguments,
                Is.EqualTo(new[] { "--wait", "", "{file}" }));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Proposed editor configuration:"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Effective executable: code"));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.EndWith("Editor configuration was updated." + Environment.NewLine));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies setup defaults to EDITOR without interpreting its current value.</summary>
    [Test]
    public async Task EditorSetup_EnvironmentSelection_DefaultsVariableName()
    {
        using var fixture = HandlerFixture.Create(
            UserConfigurationLoadResult.Missing,
            input: new[] { "1", "", "0", "y" });

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(
                fixture.Store.SavedConfiguration!.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Environment));
            Assert.That(fixture.Store.SavedConfiguration.Editor.Variable, Is.EqualTo("EDITOR"));
            Assert.That(fixture.Store.SavedConfiguration.Editor.Arguments, Is.Empty);
        }
    }

    /// <summary>Verifies setup can persist the explicit unset selection.</summary>
    [Test]
    public async Task EditorSetup_UnsetSelection_SavesExplicitState()
    {
        using var fixture = HandlerFixture.Create(
            UserConfigurationLoadResult.Missing,
            input: new[] { "3", "y" });

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(
                fixture.Store.SavedConfiguration!.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Unset));
        }
    }

    /// <summary>Verifies cancel, declined confirmation, and EOF never save configuration.</summary>
    [TestCase(new[] { "0" }, 0, "Operation was canceled.")]
    [TestCase(new[] { "3", "n" }, 0, "Operation was canceled.")]
    [TestCase(new string?[] { null }, (int)CliExitCode.Validation, "Input ended")]
    [TestCase(new string?[] { "2", null }, (int)CliExitCode.Validation, "Input ended")]
    public async Task EditorSetup_IncompleteOrCanceledInput_DoesNotSave(
        string?[] input,
        int expectedExitCode,
        string expectedMessage)
    {
        using var fixture = HandlerFixture.Create(
            Loaded(UserEditorConfiguration.FromProgram("existing")),
            input: input);

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expectedExitCode));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput + fixture.Output.StandardError,
                Does.Contain(expectedMessage));
        }
    }

    /// <summary>Verifies invalid setup input is a validation failure and preserves configuration.</summary>
    [TestCase(new[] { "9" }, "selection")]
    [TestCase(new[] { "2", "code", "-1" }, "non-negative integer")]
    [TestCase(new[] { "2", "   ", "0" }, "empty or whitespace")]
    [TestCase(new[] { "1", "BAD=NAME", "0" }, "cannot contain '='")]
    public async Task EditorSetup_InvalidInput_DoesNotSave(
        string[] input,
        string expectedMessage)
    {
        using var fixture = HandlerFixture.Create(
            Loaded(UserEditorConfiguration.FromProgram("existing")),
            input: input);

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Store.SaveCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Does.Contain(expectedMessage));
        }
    }

    /// <summary>Verifies setup refuses redirected input before reading or saving.</summary>
    [Test]
    public async Task EditorSetup_NonInteractiveInput_ReturnsValidation()
    {
        using var fixture = HandlerFixture.Create(
            UserConfigurationLoadResult.Missing,
            input: new[] { "3", "y" },
            interactive: false);

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(fixture.Store.LoadCount, Is.Zero);
            Assert.That(fixture.Store.SaveCount, Is.Zero);
        }
    }

    /// <summary>Verifies a setup conflict never overwrites the newer configuration.</summary>
    [Test]
    public async Task EditorSetup_WhenRevisionChanged_ReturnsConflict()
    {
        using var fixture = HandlerFixture.Create(
            Loaded(UserEditorConfiguration.FromProgram("existing")),
            input: new[] { "3", "y" });
        fixture.Store.SaveAsConflict = true;

        var result = await fixture.EditorSetup.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Store.SaveCount, Is.EqualTo(1));
            Assert.That(fixture.Output.StandardError, Does.Contain("Run the command again"));
        }
    }

    /// <summary>Verifies unset creates an explicit state when configuration is missing.</summary>
    [Test]
    public async Task EditorUnset_MissingConfiguration_SavesUnset()
    {
        using var fixture = HandlerFixture.Create(UserConfigurationLoadResult.Missing);

        var result = await fixture.EditorUnset.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Store.SaveCount, Is.EqualTo(1));
            Assert.That(
                fixture.Store.SavedConfiguration!.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Unset));
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Editor configuration was unset."));
        }
    }

    /// <summary>Verifies unset avoids rewriting an already-unset configuration.</summary>
    [Test]
    public async Task EditorUnset_AlreadyUnset_DoesNotSaveAgain()
    {
        using var fixture = HandlerFixture.Create(Loaded(UserEditorConfiguration.Unset));

        var result = await fixture.EditorUnset.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Store.SaveCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Is.EqualTo("Editor is already unset." + Environment.NewLine));
        }
    }

    /// <summary>Verifies save failures map to storage without reporting success.</summary>
    [Test]
    public async Task EditorUnset_WhenSaveFails_ReturnsStorageFailure()
    {
        using var fixture = HandlerFixture.Create(Loaded(
            UserEditorConfiguration.FromProgram("nano")));
        fixture.Store.SaveException = new UserConfigurationSaveException(
            "configuration save failed",
            new IOException("disk full"));

        var result = await fixture.EditorUnset.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Storage));
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain("configuration save failed"));
        }
    }

    static UserConfigurationLoadResult Loaded(UserEditorConfiguration editor)
    {
        return UserConfigurationLoadResult.FromConfiguration(
            new UserConfigurationModel(editor),
            UserConfigurationRevision.FromContent(new byte[] { 1, 2, 3 }));
    }

    sealed class HandlerFixture : IDisposable
    {
        readonly string _root;

        HandlerFixture(
            string root,
            StubUserConfigurationStore store,
            StubCommandInput input,
            RecordingCommandOutput output,
            UserConfigPathCommandHandler path,
            UserConfigShowCommandHandler show,
            UserConfigValidateCommandHandler validate,
            UserConfigEditorShowCommandHandler editorShow,
            UserConfigEditorSetupCommandHandler editorSetup,
            UserConfigEditorUnsetCommandHandler editorUnset)
        {
            _root = root;
            Store = store;
            Input = input;
            Output = output;
            Path = path;
            Show = show;
            Validate = validate;
            EditorShow = editorShow;
            EditorSetup = editorSetup;
            EditorUnset = editorUnset;
        }

        internal string ApplicationDataDirectory => System.IO.Path.Combine(
            _root,
            "application-data");

        internal string ConfigurationPath => System.IO.Path.Combine(
            ApplicationDataDirectory,
            UserConfigurationContract.FileName);

        internal StubUserConfigurationStore Store { get; }

        internal StubCommandInput Input { get; }

        internal RecordingCommandOutput Output { get; }

        internal UserConfigPathCommandHandler Path { get; }

        internal UserConfigShowCommandHandler Show { get; }

        internal UserConfigValidateCommandHandler Validate { get; }

        internal UserConfigEditorShowCommandHandler EditorShow { get; }

        internal UserConfigEditorSetupCommandHandler EditorSetup { get; }

        internal UserConfigEditorUnsetCommandHandler EditorUnset { get; }

        internal static HandlerFixture Create(
            UserConfigurationLoadResult loaded,
            string? environmentName = null,
            string? environmentValue = null,
            IEnumerable<string?>? input = null,
            bool interactive = true)
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.UserConfigurationHandlers-{Guid.NewGuid():N}");
            var paths = new ApplicationPaths(System.IO.Path.Combine(root, "application-data"));
            var store = new StubUserConfigurationStore(loaded);
            var commandInput = new StubCommandInput(input, interactive);
            var output = new RecordingCommandOutput();
            var environment = new StubEnvironmentVariableSource(
                environmentName,
                environmentValue);
            var workflow = new UserConfigurationWorkflow(
                paths,
                store,
                new UserEditorConfigurationResolver(environment));
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new HandlerFixture(
                root,
                store,
                commandInput,
                output,
                new UserConfigPathCommandHandler(executor, workflow, output),
                new UserConfigShowCommandHandler(executor, workflow, output),
                new UserConfigValidateCommandHandler(executor, workflow, output),
                new UserConfigEditorShowCommandHandler(executor, workflow, output),
                new UserConfigEditorSetupCommandHandler(
                    executor,
                    workflow,
                    commandInput,
                    output),
                new UserConfigEditorUnsetCommandHandler(executor, workflow, output));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    sealed class StubUserConfigurationStore : IUserConfigurationStore
    {
        readonly UserConfigurationLoadResult _loaded;

        internal StubUserConfigurationStore(UserConfigurationLoadResult loaded)
        {
            _loaded = loaded;
        }

        internal int LoadCount { get; private set; }

        internal int SaveCount { get; private set; }

        internal Exception? LoadException { get; set; }

        internal Exception? SaveException { get; set; }

        internal bool SaveAsConflict { get; set; }

        internal UserConfigurationModel? SavedConfiguration { get; private set; }

        internal UserConfigurationRevision? ExpectedRevision { get; private set; }

        public UserConfigurationLoadResult Load()
        {
            LoadCount++;
            if (LoadException != null)
                throw LoadException;
            return _loaded;
        }

        public UserConfigurationSaveResult Save(
            UserConfigurationModel configuration,
            UserConfigurationRevision expectedRevision)
        {
            SaveCount++;
            SavedConfiguration = configuration;
            ExpectedRevision = expectedRevision;
            if (SaveException != null)
                throw SaveException;
            if (SaveAsConflict)
                return UserConfigurationSaveResult.Conflict(_loaded);
            return UserConfigurationSaveResult.Saved(
                UserConfigurationLoadResult.FromConfiguration(
                    configuration,
                    UserConfigurationRevision.FromContent(new byte[] { 4, 5, 6 })));
        }
    }

    sealed class StubEnvironmentVariableSource : IEnvironmentVariableSource
    {
        readonly string? _name;
        readonly string? _value;

        internal StubEnvironmentVariableSource(string? name, string? value)
        {
            _name = name;
            _value = value;
        }

        public string? Get(string name)
        {
            if (_name != null)
                Assert.That(name, Is.EqualTo(_name));
            return _value;
        }
    }

    sealed class StubCommandInput : ICommandInput
    {
        readonly Queue<string?> _lines;

        internal StubCommandInput(IEnumerable<string?>? lines, bool isInteractive)
        {
            _lines = new Queue<string?>(lines ?? Array.Empty<string?>());
            IsInteractive = isInteractive;
        }

        internal int ReadCount { get; private set; }

        public bool IsInteractive { get; }

        public string? ReadLine()
        {
            ReadCount++;
            return _lines.Count == 0 ? null : _lines.Dequeue();
        }

        public Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            throw new AssertionException("Editor setup must read line-oriented input.");
        }
    }

    sealed class RecordingCommandOutput : ICommandOutput
    {
        readonly StringBuilder _standardOutput = new();
        readonly StringBuilder _standardError = new();

        internal string StandardOutput => _standardOutput.ToString();

        internal string StandardError => _standardError.ToString();

        public void Write(string value)
        {
            _standardOutput.Append(value);
        }

        public void WriteLine(string value)
        {
            _standardOutput.AppendLine(value);
        }

        public void WriteErrorLine(string value)
        {
            _standardError.AppendLine(value);
        }

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
            throw new AssertionException("User configuration handlers must not write pages.");
        }

        public void WriteWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries,
            bool longFormat)
        {
            throw new AssertionException("User configuration handlers must not write notebooks.");
        }
    }
}
