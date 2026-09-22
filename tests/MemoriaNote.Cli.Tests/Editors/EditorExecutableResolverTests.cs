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
        var configuration = CreateConfiguration("configured-editor", useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));

        var result = resolver.Resolve(configuration, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("environment-editor"));
    }

    /// <summary>Verifies that disabling environment lookup uses the configured path.</summary>
    [Test]
    public void Resolve_DisabledEnvironmentValue_UsesConfiguredPath()
    {
        var configuration = CreateConfiguration("configured-editor", useEnvironment: false);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));

        var result = resolver.Resolve(configuration, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("configured-editor"));
    }

    /// <summary>Verifies that an empty environment value falls back to configuration.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_EmptyEnvironmentValue_UsesConfiguredPath(string? environmentValue)
    {
        var configuration = CreateConfiguration("configured-editor", useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource(environmentValue));

        var result = resolver.Resolve(configuration, null);

        Assert.That(result.ExecutablePath, Is.EqualTo("configured-editor"));
    }

    /// <summary>Verifies that an unresolved editor produces a clear failure.</summary>
    [Test]
    public void Resolve_NoAvailableEditor_Throws()
    {
        var configuration = CreateConfiguration(string.Empty, useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource(null));

        Action resolve = () => resolver.Resolve(configuration, null);

        Assert.That(
            resolve,
            Throws.TypeOf<ExternalEditorConfigurationException>()
                .With.Message.EqualTo("External editor path is not configured."));
    }

    /// <summary>Verifies an invocation override bypasses environment and configuration.</summary>
    [Test]
    public void Resolve_CommandOverride_HasHighestPriority()
    {
        var configuration = CreateConfiguration("configured-editor", useEnvironment: true);
        var resolver = new EditorExecutableResolver(
            new StubEnvironmentVariableSource("environment-editor"));
        var commandOverride = new ExternalEditorCommand(
            "explicit-editor",
            new[] { "--wait" });

        var result = resolver.Resolve(configuration, commandOverride);

        Assert.That(result, Is.SameAs(commandOverride));
    }

    static ConfigurationCli CreateConfiguration(
        string editorPath,
        bool useEnvironment)
    {
        return new ConfigurationCli
        {
            Terminal = new ConfigurationCli.TerminalSetting
            {
                EditorEnv = useEnvironment,
                EditorPath = editorPath
            }
        };
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
                Is.EqualTo(ConfigurationCli.TerminalSetting.EditorEnvName));
            return _value;
        }
    }
}
