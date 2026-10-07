using MemoriaNote.Cli.UserConfig;
using NUnit.Framework;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.UserConfiguration;

/// <summary>Verifies atomic and conflict-aware user configuration persistence.</summary>
[TestFixture]
public sealed class UserConfigurationSaveTests
{
    /// <summary>Verifies explicit save creates a private directory and canonical configuration.</summary>
    [Test]
    public void Save_MissingDirectory_CreatesCanonicalConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var applicationDataDirectory = Path.Combine(directory.Path, "application-data");
        var store = CreateStore(applicationDataDirectory);
        var missing = store.Load();
        var configuration = ProgramConfiguration("code", "--wait", "{file}");

        var result = store.Save(configuration, missing.Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationSaveStatus.Saved));
            Assert.That(result.Current.Status, Is.EqualTo(UserConfigurationLoadStatus.Loaded));
            Assert.That(result.Current.Revision.IsMissing, Is.False);
            Assert.That(
                result.Current.Configuration.Editor.Executable,
                Is.EqualTo("code"));
            Assert.That(
                File.ReadAllText(ConfigurationPath(applicationDataDirectory)),
                Is.EqualTo(
                    "format_version = 1\n\n" +
                    "[editor]\n" +
                    "selection = \"program\"\n" +
                    "executable = \"code\"\n" +
                    "arguments = [\"--wait\", \"{file}\"]\n"));
            AssertNoTemporaryArtifacts(applicationDataDirectory);
        }
    }

    /// <summary>Verifies saving with the current revision atomically replaces existing content.</summary>
    [Test]
    public void Save_CurrentRevision_ReplacesAndReturnsNewRevision()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var created = store.Save(
            new UserConfigurationModel(UserEditorConfiguration.Unset),
            store.Load().Revision);

        var updated = store.Save(
            UserConfigurationModelForEnvironment("VISUAL", "--wait"),
            created.Current.Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Status, Is.EqualTo(UserConfigurationSaveStatus.Saved));
            Assert.That(updated.Current.Revision, Is.Not.EqualTo(created.Current.Revision));
            Assert.That(
                updated.Current.Configuration.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Environment));
            Assert.That(
                updated.Current.Configuration.Editor.Variable,
                Is.EqualTo("VISUAL"));
            Assert.That(
                store.Load().Revision,
                Is.EqualTo(updated.Current.Revision));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies a stale loaded revision cannot overwrite a newer configuration.</summary>
    [Test]
    public void Save_StaleRevision_ReturnsConflictWithCurrentConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        store.Save(
            new UserConfigurationModel(UserEditorConfiguration.Unset),
            store.Load().Revision);
        var firstReader = store.Load();
        var staleReader = store.Load();
        var firstUpdate = store.Save(
            ProgramConfiguration("code", "--wait"),
            firstReader.Revision);

        var conflict = store.Save(
            UserConfigurationModelForEnvironment("EDITOR"),
            staleReader.Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstUpdate.Status, Is.EqualTo(UserConfigurationSaveStatus.Saved));
            Assert.That(conflict.Status, Is.EqualTo(UserConfigurationSaveStatus.Conflict));
            Assert.That(
                conflict.Current.Revision,
                Is.EqualTo(firstUpdate.Current.Revision));
            Assert.That(
                conflict.Current.Configuration.Editor.Selection,
                Is.EqualTo(UserEditorSelection.Program));
            Assert.That(
                conflict.Current.Configuration.Editor.Executable,
                Is.EqualTo("code"));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies even a comment-only external edit invalidates a loaded revision.</summary>
    [Test]
    public void Save_CommentChangedAfterLoad_ReturnsConflictWithoutCanonicalizing()
    {
        using var directory = new TemporaryDirectory();
        const string original =
            "format_version = 1\n[editor]\nselection = \"unset\"\n";
        const string externallyEdited = original + "# external comment\n";
        File.WriteAllText(directory.ConfigurationPath, original);
        var store = CreateStore(directory.Path);
        var loaded = store.Load();
        File.WriteAllText(directory.ConfigurationPath, externallyEdited);

        var result = store.Save(ProgramConfiguration("code"), loaded.Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationSaveStatus.Conflict));
            Assert.That(result.Current.Revision, Is.Not.EqualTo(loaded.Revision));
            Assert.That(File.ReadAllText(directory.ConfigurationPath), Is.EqualTo(externallyEdited));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies simultaneous creators produce one saved result and one conflict.</summary>
    [Test]
    public async Task Save_ConcurrentCreation_PreservesTheWinner()
    {
        using var directory = new TemporaryDirectory();
        var firstStore = CreateStore(directory.Path);
        var secondStore = CreateStore(directory.Path);
        var firstMissing = firstStore.Load();
        var secondMissing = secondStore.Load();
        using var start = new ManualResetEventSlim(false);

        var firstTask = Task.Run(() =>
        {
            start.Wait();
            return firstStore.Save(
                ProgramConfiguration("first-editor"),
                firstMissing.Revision);
        });
        var secondTask = Task.Run(() =>
        {
            start.Wait();
            return secondStore.Save(
                ProgramConfiguration("second-editor"),
                secondMissing.Revision);
        });
        start.Set();

        var results = await Task.WhenAll(firstTask, secondTask);
        var saved = results.Single(result =>
            result.Status == UserConfigurationSaveStatus.Saved);
        var conflict = results.Single(result =>
            result.Status == UserConfigurationSaveStatus.Conflict);
        var final = firstStore.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Current.Status, Is.EqualTo(UserConfigurationLoadStatus.Loaded));
            Assert.That(conflict.Current.Revision, Is.EqualTo(saved.Current.Revision));
            Assert.That(final.Revision, Is.EqualTo(saved.Current.Revision));
            Assert.That(
                final.Configuration.Editor.Executable,
                Is.EqualTo(saved.Current.Configuration.Editor.Executable));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies failed post-save validation restores the exact previous file.</summary>
    [Test]
    public void Save_PostCommitValidationFails_RestoresPreviousBytes()
    {
        using var directory = new TemporaryDirectory();
        const string original =
            "# preserve exact source during recovery\n" +
            "format_version = 1\n" +
            "[editor]\n" +
            "selection = \"unset\"\n";
        File.WriteAllText(directory.ConfigurationPath, original);
        var store = CreateStore(
            directory.Path,
            path => File.WriteAllText(path, "not valid TOML\n"));
        var loaded = store.Load();

        Assert.That(
            () => store.Save(ProgramConfiguration("code"), loaded.Revision),
            Throws.TypeOf<UserConfigurationSaveException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(directory.ConfigurationPath), Is.EqualTo(original));
            Assert.That(store.Load().Revision, Is.EqualTo(loaded.Revision));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies failed verification of a new file restores the missing state.</summary>
    [Test]
    public void Save_NewFileValidationFails_RemovesUnverifiedFile()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(
            directory.Path,
            path => File.WriteAllText(path, "not valid TOML\n"));
        var missing = store.Load();

        Assert.That(
            () => store.Save(ProgramConfiguration("code"), missing.Revision),
            Throws.TypeOf<UserConfigurationSaveException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(directory.ConfigurationPath), Is.False);
            Assert.That(store.Load().Status, Is.EqualTo(UserConfigurationLoadStatus.Missing));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies coordination failure leaves the existing file byte-for-byte intact.</summary>
    [Test]
    public void Save_CoordinationLockIsDirectory_PreservesExistingFile()
    {
        using var directory = new TemporaryDirectory();
        const string original =
            "format_version = 1\n[editor]\nselection = \"unset\"\n";
        File.WriteAllText(directory.ConfigurationPath, original);
        Directory.CreateDirectory(directory.LockPath);
        var store = CreateStore(directory.Path);
        var loaded = store.Load();

        Assert.That(
            () => store.Save(ProgramConfiguration("code"), loaded.Revision),
            Throws.TypeOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(directory.ConfigurationPath), Is.EqualTo(original));
            Assert.That(store.Load().Revision, Is.EqualTo(loaded.Revision));
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies saving the new TOML does not inspect or modify legacy JSON.</summary>
    [Test]
    public void Save_LegacyJsonExists_PreservesIt()
    {
        using var directory = new TemporaryDirectory();
        var legacyPath = Path.Combine(directory.Path, "configuration.json");
        const string legacyContent = "{ not valid json }";
        File.WriteAllText(legacyPath, legacyContent);
        var store = CreateStore(directory.Path);

        var result = store.Save(
            new UserConfigurationModel(UserEditorConfiguration.Unset),
            store.Load().Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UserConfigurationSaveStatus.Saved));
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyContent));
            Assert.That(File.Exists(directory.ConfigurationPath), Is.True);
        }
    }

    /// <summary>Verifies new Unix state is accessible only by the current user.</summary>
    [Test]
    public void Save_OnUnix_CreatesPrivateDirectoryAndFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes are not available on Windows.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var applicationDataDirectory = Path.Combine(directory.Path, "application-data");
        var store = CreateStore(applicationDataDirectory);

        store.Save(
            new UserConfigurationModel(UserEditorConfiguration.Unset),
            store.Load().Revision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                File.GetUnixFileMode(applicationDataDirectory),
                Is.EqualTo(
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute));
            Assert.That(
                File.GetUnixFileMode(ConfigurationPath(applicationDataDirectory)),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(
                File.GetUnixFileMode(LockPath(applicationDataDirectory)),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
    }

    /// <summary>Verifies a configuration symlink is never replaced or followed.</summary>
    [Test]
    public void Save_SymbolicLinkConfiguration_ThrowsAndPreservesTarget()
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
        var store = CreateStore(directory.Path);

        Assert.That(
            () => store.Save(
                ProgramConfiguration("code"),
                UserConfigurationRevision.Missing),
            Throws.TypeOf<UserConfigurationFormatException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo(targetContent));
            Assert.That(new FileInfo(directory.ConfigurationPath).LinkTarget, Is.Not.Null);
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    /// <summary>Verifies a coordination-lock symlink is rejected without touching its target.</summary>
    [Test]
    public void Save_SymbolicLinkCoordinationLock_ThrowsAndPreservesTarget()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "lock-target");
        const string targetContent = "do not modify";
        File.WriteAllText(targetPath, targetContent);
        try
        {
            File.CreateSymbolicLink(directory.LockPath, targetPath);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not available on this platform.");
        }
        var store = CreateStore(directory.Path);

        Assert.That(
            () => store.Save(
                ProgramConfiguration("code"),
                UserConfigurationRevision.Missing),
            Throws.TypeOf<UserConfigurationFormatException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo(targetContent));
            Assert.That(new FileInfo(directory.LockPath).LinkTarget, Is.Not.Null);
            Assert.That(File.Exists(directory.ConfigurationPath), Is.False);
            AssertNoTemporaryArtifacts(directory.Path);
        }
    }

    static UserConfigurationStore CreateStore(
        string applicationDataDirectory,
        Action<string>? afterCommit = null)
    {
        return new UserConfigurationStore(
            new ApplicationPaths(applicationDataDirectory),
            afterCommit!);
    }

    static UserConfigurationModel ProgramConfiguration(
        string executable,
        params string[] arguments)
    {
        return new UserConfigurationModel(
            UserEditorConfiguration.FromProgram(executable, arguments));
    }

    static UserConfigurationModel UserConfigurationModelForEnvironment(
        string variable,
        params string[] arguments)
    {
        return new UserConfigurationModel(
            UserEditorConfiguration.FromEnvironment(variable, arguments));
    }

    static string ConfigurationPath(string applicationDataDirectory)
    {
        return Path.Combine(
            applicationDataDirectory,
            UserConfigurationContract.FileName);
    }

    static string LockPath(string applicationDataDirectory)
    {
        return Path.Combine(
            applicationDataDirectory,
            $".{UserConfigurationContract.FileName}.lock");
    }

    static void AssertNoTemporaryArtifacts(string applicationDataDirectory)
    {
        Assert.That(
            Directory.EnumerateFiles(applicationDataDirectory, "*.tmp"),
            Is.Empty);
        Assert.That(
            Directory.EnumerateFiles(applicationDataDirectory, "*.bak"),
            Is.Empty);
    }

    sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MemoriaNote.UserConfigurationSaveTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string ConfigurationPath => UserConfigurationSaveTests.ConfigurationPath(Path);

        internal string LockPath => UserConfigurationSaveTests.LockPath(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
