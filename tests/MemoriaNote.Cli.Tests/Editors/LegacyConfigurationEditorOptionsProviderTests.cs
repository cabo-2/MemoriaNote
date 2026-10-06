using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies the temporary adapter from legacy CLI configuration.</summary>
[TestFixture]
public sealed class LegacyConfigurationEditorOptionsProviderTests
{
    /// <summary>Verifies that only editor selection values cross the adapter boundary.</summary>
    [Test]
    public void Load_LegacyConfiguration_ReturnsEditorOptions()
    {
        var configuration = new ConfigurationCli
        {
            Terminal = new ConfigurationCli.TerminalSetting
            {
                EditorEnv = false,
                EditorPath = "configured-editor"
            }
        };
        var contextFactory = new StubCommandContextFactory(configuration);
        var provider = new LegacyConfigurationEditorOptionsProvider(contextFactory);

        var result = provider.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contextFactory.LoadCount, Is.EqualTo(1));
            Assert.That(result.UseEnvironmentVariable, Is.False);
            Assert.That(result.EnvironmentVariableName, Is.EqualTo("EDITOR"));
            Assert.That(result.ExecutablePath, Is.EqualTo("configured-editor"));
        }
    }

    /// <summary>Verifies that missing legacy editor settings cross the adapter explicitly.</summary>
    [Test]
    public void Load_MissingTerminalSettings_ReturnsMissingOptions()
    {
        var configuration = new ConfigurationCli { Terminal = null };
        var provider = new LegacyConfigurationEditorOptionsProvider(
            new StubCommandContextFactory(configuration));

        var result = provider.Load();

        Assert.That(result.HasSettings, Is.False);
    }

    sealed class StubCommandContextFactory : ICliCommandContextFactory
    {
        readonly ConfigurationCli _configuration;

        internal StubCommandContextFactory(ConfigurationCli configuration)
        {
            _configuration = configuration;
        }

        internal int LoadCount { get; private set; }

        public ConfigurationCli LoadConfiguration()
        {
            LoadCount++;
            return _configuration;
        }

        public void SaveConfiguration(ConfigurationCli configuration)
        {
            throw new AssertionException("The adapter must not save legacy configuration.");
        }
    }
}
