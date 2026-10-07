using System.Text;
using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies read-only access to the versioned user configuration file.</summary>
[TestFixture]
public sealed class UserConfigurationStoreTests
{
    /// <summary>Verifies a missing application data directory remains absent after loading.</summary>
    [Test]
    public void Load_MissingDirectory_ReturnsMissingWithoutCreatingState()
    {
        using var directory = new TemporaryDirectory();
        var applicationDataDirectory = Path.Combine(directory.Path, "application-data");
        var store = new UserConfigurationStore(
            new ApplicationPaths(applicationDataDirectory));

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationLoadStatus.Missing));
            Assert.That(result.Configuration, Is.Null);
            Assert.That(Directory.Exists(applicationDataDirectory), Is.False);
        }
    }

    /// <summary>Verifies a valid TOML file is loaded from the application data directory.</summary>
    [Test]
    public void Load_ValidConfiguration_ReturnsLoadedModel()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            directory.ConfigurationPath,
            "format_version = 1\n[editor]\nselection = \"program\"\n" +
            "executable = \"code\"\narguments = [\"--wait\"]\n");
        var store = directory.CreateStore();

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationLoadStatus.Loaded));
            Assert.That(
                result.Configuration.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Program));
            Assert.That(result.Configuration.Editor.Executable, Is.EqualTo("code"));
            Assert.That(result.Configuration.Editor.Arguments, Is.EqualTo(new[] { "--wait" }));
        }
    }

    /// <summary>Verifies legacy JSON neither supplies nor creates the new configuration.</summary>
    [Test]
    public void Load_OnlyLegacyJsonExists_IgnoresItWithoutChanges()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = Path.Combine(directory.Path, "configuration.json");
        const string legacyContent = "{ not valid json }";
        File.WriteAllText(legacyPath, legacyContent);
        var store = directory.CreateStore();

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationLoadStatus.Missing));
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyContent));
            Assert.That(File.Exists(directory.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies the new TOML is authoritative when both formats exist.</summary>
    [Test]
    public void Load_NewAndLegacyConfigurationsExist_LoadsTomlAndPreservesJson()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = Path.Combine(directory.Path, "configuration.json");
        const string legacyContent = "{ not valid json }";
        File.WriteAllText(legacyPath, legacyContent);
        File.WriteAllText(
            directory.ConfigurationPath,
            "format_version = 1\n[editor]\nselection = \"unset\"\n");
        var store = directory.CreateStore();

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationLoadStatus.Loaded));
            Assert.That(
                result.Configuration.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Unset));
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyContent));
        }
    }

    /// <summary>Verifies invalid TOML is not hidden by a legacy JSON file.</summary>
    [Test]
    public void Load_InvalidTomlAndLegacyJsonExist_ThrowsWithoutFallback()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = Path.Combine(directory.Path, "configuration.json");
        const string legacyContent = "{}";
        const string invalidToml =
            "format_version = 1\n[editor]\nselection = \"program\"\n";
        File.WriteAllText(legacyPath, legacyContent);
        File.WriteAllText(directory.ConfigurationPath, invalidToml);
        var store = directory.CreateStore();

        Assert.That(
            () => store.Load(),
            Throws.TypeOf<UserConfigurationFormatException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyContent));
            Assert.That(File.ReadAllText(directory.ConfigurationPath), Is.EqualTo(invalidToml));
        }
    }

    /// <summary>Verifies invalid UTF-8 is rejected without modifying source bytes.</summary>
    [Test]
    public void Load_InvalidUtf8_ThrowsAndPreservesSource()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0xff, 0xfe, 0xfd };
        File.WriteAllBytes(directory.ConfigurationPath, bytes);
        var store = directory.CreateStore();

        Assert.That(
            () => store.Load(),
            Throws.TypeOf<UserConfigurationFormatException>());
        Assert.That(File.ReadAllBytes(directory.ConfigurationPath), Is.EqualTo(bytes));
    }

    /// <summary>Verifies non-canonical text framing is rejected without rewriting source.</summary>
    [TestCase("\ufeffformat_version = 1\n[editor]\nselection = \"unset\"\n")]
    [TestCase("format_version = 1\r\n[editor]\r\nselection = \"unset\"\r\n")]
    [TestCase("format_version = 1\n[editor]\nselection = \"unset\"")]
    public void Load_InvalidTextFraming_ThrowsAndPreservesSource(string content)
    {
        using var directory = new TemporaryDirectory();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        File.WriteAllBytes(directory.ConfigurationPath, bytes);
        var store = directory.CreateStore();

        Assert.That(
            () => store.Load(),
            Throws.TypeOf<UserConfigurationFormatException>());
        Assert.That(File.ReadAllBytes(directory.ConfigurationPath), Is.EqualTo(bytes));
    }

    /// <summary>Verifies unsupported versions remain distinct from malformed configuration.</summary>
    [Test]
    public void Load_FutureVersion_ThrowsUnsupportedVersion()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            directory.ConfigurationPath,
            "format_version = 2\n[editor]\nselection = \"unset\"\n");
        var store = directory.CreateStore();

        var exception = Assert.Throws<UnsupportedUserConfigurationVersionException>(
            () => store.Load());

        Assert.That(exception!.FormatVersion, Is.EqualTo(2));
    }

    /// <summary>Verifies a directory cannot masquerade as the configuration file.</summary>
    [Test]
    public void Load_ConfigurationPathIsDirectory_Throws()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.ConfigurationPath);
        var store = directory.CreateStore();

        Assert.That(
            () => store.Load(),
            Throws.TypeOf<UserConfigurationFormatException>());
    }

    /// <summary>Verifies a configuration symlink is rejected without reading its target.</summary>
    [Test]
    public void Load_SymbolicLinkConfiguration_ThrowsAndPreservesTarget()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "target.toml");
        const string targetContent =
            "format_version = 1\n[editor]\nselection = \"unset\"\n";
        File.WriteAllText(targetPath, targetContent);
        try
        {
            File.CreateSymbolicLink(directory.ConfigurationPath, targetPath);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not available on this platform.");
        }
        var store = directory.CreateStore();

        Assert.That(
            () => store.Load(),
            Throws.TypeOf<UserConfigurationFormatException>());
        Assert.That(File.ReadAllText(targetPath), Is.EqualTo(targetContent));
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.UserConfigurationStoreTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string ConfigurationPath => System.IO.Path.Combine(
            Path,
            UserConfigurationContract.FileName);

        internal UserConfigurationStore CreateStore()
        {
            return new UserConfigurationStore(new ApplicationPaths(Path));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
