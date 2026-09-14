using Newtonsoft.Json;
using NUnit.Framework;
using MemoriaNote.Core.Tests.Infrastructure;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies format-independent configuration persistence and recovery behavior.
/// </summary>
[TestFixture]
[Category("Functional")]
public sealed class ConfigurationStoreTests
{
    /// <summary>
    /// Verifies that a missing file is populated with default configuration.
    /// </summary>
    [Test]
    public void Load_MissingFile_CreatesAndPersistsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var store = CreateStore(paths);

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ConfigurationLoadStatus.CreatedDefault));
            Assert.That(result.RecoveryArtifactPath, Is.Null);
            Assert.That(File.Exists(paths.ConfigurationPath), Is.True);
            Assert.That(
                result.Configuration.DataSources,
                Is.EqualTo(new[] { paths.DefaultNotebookDatabasePath }));
            Assert.That(
                result.Configuration.Workspace.NotebookDatabasePaths,
                Is.EqualTo(new[] { paths.DefaultNotebookDatabasePath }));
        }
    }

    /// <summary>
    /// Verifies that saving replaces a complete existing configuration.
    /// </summary>
    [Test]
    public void Save_ExistingFile_AtomicallyReplacesContent()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var store = CreateStore(paths);
        var configuration = ConfigurationDefaults.Create<Configuration>(paths);
        store.Save(configuration);

        configuration.DefaultNotebookTitle = "Updated title";
        store.Save(configuration);

        var loaded = store.Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Status, Is.EqualTo(ConfigurationLoadStatus.Loaded));
            Assert.That(
                loaded.Configuration.DefaultNotebookTitle,
                Is.EqualTo("Updated title"));
            Assert.That(
                Directory.EnumerateFiles(directory.Path, "*.tmp"),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that malformed or null content is preserved before defaults are restored.
    /// </summary>
    /// <param name="invalidContent">The invalid serialized content.</param>
    [TestCase("{")]
    [TestCase("null")]
    public void Load_InvalidContent_QuarantinesOriginalAndRestoresDefaults(
        string invalidContent)
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(paths.ConfigurationPath, invalidContent);
        var store = CreateStore(paths);

        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(ConfigurationLoadStatus.RecoveredInvalid));
            Assert.That(result.RecoveryArtifactPath, Is.Not.Null);
            Assert.That(File.Exists(result.RecoveryArtifactPath), Is.True);
            Assert.That(
                File.ReadAllText(result.RecoveryArtifactPath!),
                Is.EqualTo(invalidContent));
            Assert.That(File.Exists(paths.ConfigurationPath), Is.True);
            Assert.That(
                store.Load().Configuration.DefaultNotebookName,
                Is.EqualTo("note"));
        }
    }

    /// <summary>Verifies that recovery artifact names use the injected UTC clock.</summary>
    [Test]
    public void Load_InvalidContent_UsesInjectedClockForRecoveryName()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(paths.ConfigurationPath, "{");
        var clock = new FixedClock(
            new DateTimeOffset(2026, 9, 14, 13, 4, 5, TimeSpan.Zero));
        var store = new FileConfigurationStore<Configuration>(
            paths.ConfigurationPath,
            new JsonConfigurationSerializer<Configuration>(),
            () => ConfigurationDefaults.Create<Configuration>(paths),
            clock);

        var result = store.Load();

        Assert.That(
            Path.GetFileName(result.RecoveryArtifactPath),
            Is.EqualTo("configuration.json.corrupt-20260914T1304050000000Z"));
    }

    /// <summary>
    /// Verifies that the file store is independent of the selected text format.
    /// </summary>
    [Test]
    public void Store_CustomSerializer_RoundTripsWithoutJsonDependency()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path, "configuration.txt");
        var store = new FileConfigurationStore<SimpleConfiguration>(
            paths.ConfigurationPath,
            new SimpleConfigurationSerializer(),
            () => new SimpleConfiguration("default"));

        store.Save(new SimpleConfiguration("custom-format"));
        var result = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                File.ReadAllText(paths.ConfigurationPath),
                Is.EqualTo("custom-format"));
            Assert.That(result.Configuration.Value, Is.EqualTo("custom-format"));
        }
    }

    /// <summary>
    /// Verifies that serialization failure leaves an existing file unchanged.
    /// </summary>
    [Test]
    public void Save_SerializationFails_PreservesExistingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "configuration.json");
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(path, "original content");
        var store = new FileConfigurationStore<Configuration>(
            path,
            new FailingSerializer(),
            () => new Configuration());

        Action save = () => store.Save(new Configuration());

        Assert.Throws<JsonSerializationException>(save);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("original content"));
            Assert.That(
                Directory.EnumerateFiles(directory.Path, "*.tmp"),
                Is.Empty);
        }
    }

    /// <summary>
    /// Verifies that the persisted configuration graph does not require ReactiveUI.
    /// </summary>
    [Test]
    public void ConfigurationTypes_ArePlainObjects()
    {
        var configurationTypes = new[]
        {
            typeof(Configuration),
            typeof(Configuration.LoggingSetting),
            typeof(Configuration.SearchSetting),
            typeof(WorkspaceSettings)
        };

        Assert.That(
            configurationTypes.Select(type => type.BaseType),
            Is.All.EqualTo(typeof(object)));
    }

    static FileConfigurationStore<Configuration> CreateStore(ApplicationPaths paths)
    {
        return new FileConfigurationStore<Configuration>(
            paths.ConfigurationPath,
            new JsonConfigurationSerializer<Configuration>(),
            () => ConfigurationDefaults.Create<Configuration>(paths));
    }

    sealed class SimpleConfiguration
    {
        internal SimpleConfiguration(string value)
        {
            Value = value;
        }

        internal string Value { get; }
    }

    sealed class SimpleConfigurationSerializer
        : IConfigurationSerializer<SimpleConfiguration>
    {
        public string Serialize(SimpleConfiguration configuration)
        {
            return configuration.Value;
        }

        public SimpleConfiguration Deserialize(string serializedConfiguration)
        {
            return new SimpleConfiguration(serializedConfiguration);
        }
    }

    sealed class FailingSerializer : IConfigurationSerializer<Configuration>
    {
        public string Serialize(Configuration configuration)
        {
            throw new JsonSerializationException("Serialization failed.");
        }

        public Configuration Deserialize(string serializedConfiguration)
        {
            throw new NotSupportedException();
        }
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.ConfigurationStoreTests-{Guid.NewGuid():N}");
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
