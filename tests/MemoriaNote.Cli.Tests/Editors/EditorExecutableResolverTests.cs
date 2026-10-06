using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies external editor executable selection.</summary>
[TestFixture]
public sealed class EditorExecutableResolverTests
{
    /// <summary>Verifies that an enabled non-empty environment value has priority.</summary>
    [Test]
    public void Resolve_EnabledEnvironmentValue_HasPriority()
    {
        var options = CreateOptions("configured-editor", useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));

        var result = resolver.Resolve(options, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("environment-editor"));
    }

    /// <summary>Verifies that disabling environment lookup uses the configured path.</summary>
    [Test]
    public void Resolve_DisabledEnvironmentValue_UsesConfiguredPath()
    {
        var options = CreateOptions("configured-editor", useEnvironment: false);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));

        var result = resolver.Resolve(options, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("configured-editor"));
    }

    /// <summary>Verifies that an empty environment value falls back to configuration.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_EmptyEnvironmentValue_UsesConfiguredPath(string? environmentValue)
    {
        var options = CreateOptions("configured-editor", useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource(environmentValue));

        var result = resolver.Resolve(options, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("configured-editor"));
    }

    /// <summary>Verifies that an unresolved editor produces a clear failure.</summary>
    [Test]
    public void Resolve_NoAvailableEditor_Throws()
    {
        var options = CreateOptions(string.Empty, useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource(null));

        Action resolve = () => resolver.Resolve(options, null);

        Assert.That(
            resolve,
            Throws.TypeOf<ExternalEditorConfigurationException>()
                .With.Message.EqualTo("External editor path is not configured."));
    }

    /// <summary>Verifies an invocation override bypasses environment and configuration.</summary>
    [Test]
    public void Resolve_CommandOverride_HasHighestPriority()
    {
        var options = EditorOptions.Missing;
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));
        var commandOverride = new ExternalEditorCommand(
            "explicit-editor",
            new[] { "--wait" });

        var result = resolver.Resolve(options, commandOverride);

        Assert.That(result, Is.SameAs(commandOverride));
    }

    /// <summary>Verifies that missing editor settings retain their clear failure.</summary>
    [Test]
    public void Resolve_MissingSettings_Throws()
    {
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));

        Action resolve = () => resolver.Resolve(EditorOptions.Missing, null);

        Assert.That(
            resolve,
            Throws.TypeOf<ExternalEditorConfigurationException>()
                .With.Message.EqualTo("External editor settings are missing."));
    }

    static EditorOptions CreateOptions(
        string editorPath,
        bool useEnvironment)
    {
        return new EditorOptions(useEnvironment, "EDITOR", editorPath);
    }

    sealed class StubEnvironmentVariableSource : IEnvironmentVariableSource
    {
        readonly string? _value;

        internal StubEnvironmentVariableSource(string? value)
        {
            _value = value;
        }

        public string? Get(string name)
        {
            Assert.That(
                name,
                Is.EqualTo("EDITOR"));
            return _value;
        }
    }
}
