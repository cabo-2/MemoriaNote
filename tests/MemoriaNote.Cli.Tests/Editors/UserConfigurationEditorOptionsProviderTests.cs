using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.Editors;

/// <summary>Verifies the active editor options provider backed by user configuration.</summary>
[TestFixture]
public sealed class UserConfigurationEditorOptionsProviderTests
{
    /// <summary>Verifies program configuration becomes one resolved command.</summary>
    [Test]
    public void Load_ProgramConfiguration_PreservesExecutableAndArguments()
    {
        var provider = CreateProvider(
            UserConfigurationLoadResult.FromConfiguration(
                new UserConfigurationModel(
                    UserEditorConfiguration.FromProgram(
                        "code",
                        new[] { "--wait", "{file}" })),
                UserConfigurationRevision.FromContent(new byte[] { 1 })));

        var options = provider.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.ResolvedCommand?.ExecutablePath, Is.EqualTo("code"));
            Assert.That(
                options.ResolvedCommand?.Arguments,
                Is.EqualTo(new[] { "--wait", "{file}" }));
        }
    }

    /// <summary>Verifies missing configuration directs the user to setup.</summary>
    [Test]
    public void Load_MissingConfiguration_ThrowsSetupGuidance()
    {
        var provider = CreateProvider(UserConfigurationLoadResult.Missing);

        Assert.That(
            provider.Load,
            Throws.TypeOf<ExternalEditorConfigurationException>()
                .With.Message.Contains("mn config editor setup"));
    }

    /// <summary>Verifies explicit unset uses the same setup guidance.</summary>
    [Test]
    public void Load_UnsetConfiguration_ThrowsSetupGuidance()
    {
        var provider = CreateProvider(
            UserConfigurationLoadResult.FromConfiguration(
                new UserConfigurationModel(UserEditorConfiguration.Unset),
                UserConfigurationRevision.FromContent(new byte[] { 2 })));

        Assert.That(
            provider.Load,
            Throws.TypeOf<ExternalEditorConfigurationException>()
                .With.Message.Contains("mn config editor setup"));
    }

    static UserConfigurationEditorOptionsProvider CreateProvider(
        UserConfigurationLoadResult loaded)
    {
        var paths = new ApplicationPaths(
            Path.Combine(Path.GetTempPath(), "editor-options-provider-tests"));
        var workflow = new UserConfigurationWorkflow(
            paths,
            new StubUserConfigurationStore(loaded),
            new UserEditorConfigurationResolver(
                new StubEnvironmentVariableSource()));
        return new UserConfigurationEditorOptionsProvider(workflow);
    }

    sealed class StubUserConfigurationStore : IUserConfigurationStore
    {
        readonly UserConfigurationLoadResult _loaded;

        internal StubUserConfigurationStore(UserConfigurationLoadResult loaded)
        {
            _loaded = loaded;
        }

        public UserConfigurationLoadResult Load()
        {
            return _loaded;
        }

        public UserConfigurationSaveResult Save(
            UserConfigurationModel configuration,
            UserConfigurationRevision expectedRevision)
        {
            throw new NotSupportedException();
        }
    }

    sealed class StubEnvironmentVariableSource : IEnvironmentVariableSource
    {
        public string? Get(string name)
        {
            return null;
        }
    }
}
