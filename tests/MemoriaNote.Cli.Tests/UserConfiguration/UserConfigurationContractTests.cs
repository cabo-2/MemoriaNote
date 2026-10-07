using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies the versioned user configuration contract.</summary>
[TestFixture]
public sealed class UserConfigurationContractTests
{
    /// <summary>Verifies the new TOML resides beside the legacy JSON under a distinct name.</summary>
    [Test]
    public void GetPath_UsesExistingApplicationDataDirectory()
    {
        var applicationDataDirectory = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.UserConfigurationContractTests");
        var applicationPaths = new ApplicationPaths(applicationDataDirectory);

        var path = UserConfigurationContract.GetPath(applicationPaths);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                path,
                Is.EqualTo(Path.Combine(applicationDataDirectory, "config.toml")));
            Assert.That(
                Path.GetDirectoryName(path),
                Is.EqualTo(Path.GetDirectoryName(applicationPaths.ConfigurationPath)));
            Assert.That(path, Is.Not.EqualTo(applicationPaths.ConfigurationPath));
            Assert.That(
                Path.GetFileName(applicationPaths.ConfigurationPath),
                Is.EqualTo("configuration.json"));
        }
    }

    /// <summary>Verifies the initial contract version and exact persisted selection values.</summary>
    [Test]
    public void VersionAndSelections_HaveStablePersistedValues()
    {
        var configuration = new UserConfigurationModel(
            UserEditorConfiguration.Unset);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.FormatVersion, Is.EqualTo(1));
            Assert.That(
                UserConfigurationContract.GetSelectionValue(
                    UserEditorSelection.Environment),
                Is.EqualTo("environment"));
            Assert.That(
                UserConfigurationContract.GetSelectionValue(
                    UserEditorSelection.Program),
                Is.EqualTo("program"));
            Assert.That(
                UserConfigurationContract.GetSelectionValue(
                    UserEditorSelection.Unset),
                Is.EqualTo("unset"));
        }
    }

    /// <summary>Verifies an unknown selection cannot be assigned a persisted value.</summary>
    [Test]
    public void GetSelectionValue_UnknownSelection_Throws()
    {
        Assert.That(
            () => UserConfigurationContract.GetSelectionValue(
                (UserEditorSelection)int.MaxValue),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
