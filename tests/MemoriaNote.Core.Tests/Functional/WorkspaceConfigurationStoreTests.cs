using System.Text;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies versioned workspace selection persistence.</summary>
[TestFixture]
public sealed class WorkspaceConfigurationStoreTests
{
    /// <summary>Verifies a missing file represents root without creating state.</summary>
    [Test]
    public void Load_MissingFile_ReturnsImplicitRootWithoutCreatingFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new WorkspaceConfigurationStore();

        var result = store.Load(directory.Path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(WorkspaceConfigurationLoadStatus.Missing));
            Assert.That(result.Configuration.CurrentNotebook, Is.Null);
            Assert.That(File.Exists(directory.ConfigurationPath), Is.False);
        }
    }

    /// <summary>Verifies selected and root states use canonical TOML.</summary>
    [Test]
    public void Save_SelectedThenRoot_ReplacesWithCanonicalConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var store = new WorkspaceConfigurationStore();

        store.Save(
            directory.Path,
            WorkspaceConfiguration.CreateSelected(NotebookFileName.FromInput("work")));
        var selectedContent = File.ReadAllText(directory.ConfigurationPath);
        var selected = store.Load(directory.Path);

        store.Save(directory.Path, WorkspaceConfiguration.CreateRoot());
        var rootContent = File.ReadAllText(directory.ConfigurationPath);
        var root = store.Load(directory.Path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                selectedContent,
                Is.EqualTo("format_version = 1\ncurrent_notebook = \"work.mnote\"\n"));
            Assert.That(selected.Status, Is.EqualTo(WorkspaceConfigurationLoadStatus.Loaded));
            Assert.That(selected.Configuration.CurrentNotebook?.Value, Is.EqualTo("work.mnote"));
            Assert.That(rootContent, Is.EqualTo("format_version = 1\n"));
            Assert.That(root.Configuration.CurrentNotebook, Is.Null);
            Assert.That(
                Directory.EnumerateFiles(directory.Path, "*.tmp"),
                Is.Empty);
        }
    }

    /// <summary>Verifies unknown fields and tables are tolerated and may be discarded on save.</summary>
    [Test]
    public void Load_UnknownFields_IgnoresThem()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            directory.ConfigurationPath,
            "# future data\nformat_version = 1\ncurrent_notebook = \"work.mnote\"\n" +
            "future_flag = true\n[future]\nvalues = [1, 2]\n");
        var store = new WorkspaceConfigurationStore();

        var result = store.Load(directory.Path);
        store.Save(directory.Path, result.Configuration);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Configuration.CurrentNotebook?.Value, Is.EqualTo("work.mnote"));
            Assert.That(
                File.ReadAllText(directory.ConfigurationPath),
                Is.EqualTo("format_version = 1\ncurrent_notebook = \"work.mnote\"\n"));
        }
    }

    /// <summary>Verifies invalid TOML and invalid known values fail without rewriting source.</summary>
    [TestCase("format_version =")]
    [TestCase("current_notebook = \"work.mnote\"")]
    [TestCase("format_version = \"1\"")]
    [TestCase("format_version = 0")]
    [TestCase("format_version = 1\ncurrent_notebook = 1")]
    [TestCase("format_version = 1\ncurrent_notebook = \"work\"")]
    [TestCase("format_version = 1\ncurrent_notebook = \"--root.mnote\"")]
    public void Load_InvalidConfiguration_ThrowsAndPreservesSource(string content)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.ConfigurationPath, content);
        var store = new WorkspaceConfigurationStore();

        Assert.That(
            () => store.Load(directory.Path),
            Throws.TypeOf<WorkspaceConfigurationFormatException>());
        Assert.That(File.ReadAllText(directory.ConfigurationPath), Is.EqualTo(content));
    }

    /// <summary>Verifies newer configuration versions have a distinct diagnostic.</summary>
    [Test]
    public void Load_NewerVersion_ThrowsUnsupportedVersion()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(directory.ConfigurationPath, "format_version = 2\n");
        var store = new WorkspaceConfigurationStore();

        var exception = Assert.Throws<UnsupportedWorkspaceConfigurationVersionException>(
            () => store.Load(directory.Path));

        Assert.That(exception!.FormatVersion, Is.EqualTo(2));
    }

    /// <summary>Verifies malformed UTF-8 is reported as invalid configuration.</summary>
    [Test]
    public void Load_InvalidUtf8_ThrowsFormatException()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(directory.ConfigurationPath, [0xff]);
        var store = new WorkspaceConfigurationStore();

        Assert.That(
            () => store.Load(directory.Path),
            Throws.TypeOf<WorkspaceConfigurationFormatException>());
    }

    /// <summary>Verifies a configuration symlink is rejected without changing its target.</summary>
    [Test]
    public void Save_SymbolicLinkConfiguration_ThrowsAndPreservesTarget()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "target.toml");
        const string targetContent = "format_version = 1\nfuture = true\n";
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
        var store = new WorkspaceConfigurationStore();

        Assert.That(
            () => store.Save(directory.Path, WorkspaceConfiguration.CreateRoot()),
            Throws.TypeOf<WorkspaceConfigurationFormatException>());
        Assert.That(File.ReadAllText(targetPath), Is.EqualTo(targetContent));
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.WorkspaceConfigurationStoreTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string ConfigurationPath =>
            System.IO.Path.Combine(Path, WorkspaceConfigurationStore.FileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
