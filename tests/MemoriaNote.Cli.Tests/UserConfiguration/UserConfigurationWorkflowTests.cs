using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies the dormant user configuration workflow boundary.</summary>
[TestFixture]
public sealed class UserConfigurationWorkflowTests
{
    /// <summary>Verifies that a missing file remains distinct from an explicit unset selection.</summary>
    [Test]
    public void Inspect_MissingConfiguration_ReturnsUnconfiguredEditor()
    {
        var store = new StubUserConfigurationStore(UserConfigurationLoadResult.Missing);
        var workflow = CreateWorkflow(store, new StubEnvironmentVariableSource());

        var result = workflow.Inspect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Loaded.Status, Is.EqualTo(UserConfigurationLoadStatus.Missing));
            Assert.That(result.Editor.Configuration, Is.Null);
            Assert.That(result.Editor.Source, Is.EqualTo("none"));
            Assert.That(result.Editor.EffectiveCommand, Is.Null);
            Assert.That(store.LoadCount, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies that program selection preserves executable and argument boundaries.</summary>
    [Test]
    public void Inspect_ProgramSelection_ReturnsEffectiveCommand()
    {
        var editor = UserEditorConfiguration.FromProgram(
            "code",
            new[] { "--wait", "", "{file}" });
        var store = new StubUserConfigurationStore(Loaded(editor));
        var workflow = CreateWorkflow(store, new StubEnvironmentVariableSource());

        var result = workflow.Inspect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Editor.Configuration, Is.SameAs(editor));
            Assert.That(result.Editor.Source, Is.EqualTo("user configuration"));
            Assert.That(result.Editor.EffectiveCommand!.ExecutablePath, Is.EqualTo("code"));
            Assert.That(
                result.Editor.EffectiveCommand.Arguments,
                Is.EqualTo(new[] { "--wait", "", "{file}" }));
        }
    }

    /// <summary>Verifies that environment selection uses one value as one executable.</summary>
    [Test]
    public void Inspect_EnvironmentSelection_UsesExactEnvironmentValue()
    {
        var editor = UserEditorConfiguration.FromEnvironment(
            "VISUAL",
            new[] { "--wait" });
        var store = new StubUserConfigurationStore(Loaded(editor));
        var environment = new StubEnvironmentVariableSource
        {
            Name = "VISUAL",
            Value = "editor with spaces"
        };
        var workflow = CreateWorkflow(store, environment);

        var result = workflow.Inspect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(environment.GetCount, Is.EqualTo(1));
            Assert.That(result.Editor.Source, Is.EqualTo("environment variable VISUAL"));
            Assert.That(
                result.Editor.EffectiveCommand!.ExecutablePath,
                Is.EqualTo("editor with spaces"));
            Assert.That(result.Editor.EffectiveCommand.Arguments, Is.EqualTo(new[] { "--wait" }));
        }
    }

    /// <summary>Verifies that an empty selected environment variable has no effective command.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Inspect_EnvironmentSelectionWithoutValue_RemainsUnconfigured(string? value)
    {
        var editor = UserEditorConfiguration.FromEnvironment("EDITOR");
        var store = new StubUserConfigurationStore(Loaded(editor));
        var workflow = CreateWorkflow(
            store,
            new StubEnvironmentVariableSource { Name = "EDITOR", Value = value });

        var result = workflow.Inspect();

        Assert.That(result.Editor.EffectiveCommand, Is.Null);
    }

    /// <summary>Verifies that explicit unset remains a loaded persisted state.</summary>
    [Test]
    public void Inspect_UnsetSelection_RemainsLoadedWithoutCommand()
    {
        var store = new StubUserConfigurationStore(Loaded(UserEditorConfiguration.Unset));
        var workflow = CreateWorkflow(store, new StubEnvironmentVariableSource());

        var result = workflow.Inspect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Loaded.Status, Is.EqualTo(UserConfigurationLoadStatus.Loaded));
            Assert.That(
                result.Editor.Configuration!.Selection,
                Is.EqualTo(UserEditorSelection.Unset));
            Assert.That(result.Editor.Source, Is.EqualTo("user configuration"));
            Assert.That(result.Editor.EffectiveCommand, Is.Null);
        }
    }

    /// <summary>Verifies that saving carries the caller's observed revision into the store.</summary>
    [Test]
    public void SaveEditor_PassesConfigurationAndExpectedRevision()
    {
        var loaded = Loaded(UserEditorConfiguration.Unset);
        var store = new StubUserConfigurationStore(loaded);
        var workflow = CreateWorkflow(store, new StubEnvironmentVariableSource());
        var editor = UserEditorConfiguration.FromProgram("nano");

        var result = workflow.SaveEditor(editor, loaded.Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationSaveStatus.Saved));
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.SavedConfiguration!.Editor, Is.SameAs(editor));
            Assert.That(store.ExpectedRevision, Is.SameAs(loaded.Revision));
        }
    }

    static UserConfigurationWorkflow CreateWorkflow(
        IUserConfigurationStore store,
        IEnvironmentVariableSource environment)
    {
        return new UserConfigurationWorkflow(
            new ApplicationPaths(Path.Combine(Path.GetTempPath(), "workflow-tests")),
            store,
            new UserEditorConfigurationResolver(environment));
    }

    static UserConfigurationLoadResult Loaded(UserEditorConfiguration editor)
    {
        return UserConfigurationLoadResult.FromConfiguration(
            new UserConfigurationModel(editor),
            UserConfigurationRevision.FromContent(new byte[] { 1, 2, 3 }));
    }

    sealed class StubEnvironmentVariableSource : IEnvironmentVariableSource
    {
        internal string? Name { get; init; }

        internal string? Value { get; init; }

        internal int GetCount { get; private set; }

        public string? Get(string name)
        {
            GetCount++;
            if (Name != null)
                Assert.That(name, Is.EqualTo(Name));
            return Value;
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

        internal UserConfigurationModel? SavedConfiguration { get; private set; }

        internal UserConfigurationRevision? ExpectedRevision { get; private set; }

        public UserConfigurationLoadResult Load()
        {
            LoadCount++;
            return _loaded;
        }

        public UserConfigurationSaveResult Save(
            UserConfigurationModel configuration,
            UserConfigurationRevision expectedRevision)
        {
            SaveCount++;
            SavedConfiguration = configuration;
            ExpectedRevision = expectedRevision;
            return UserConfigurationSaveResult.Saved(
                UserConfigurationLoadResult.FromConfiguration(
                    configuration,
                    UserConfigurationRevision.FromContent(new byte[] { 4, 5, 6 })));
        }
    }
}
