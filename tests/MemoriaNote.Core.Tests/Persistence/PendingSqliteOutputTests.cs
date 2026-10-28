using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence;

/// <summary>Verifies private SQLite output publication and cleanup.</summary>
[TestFixture]
public sealed class PendingSqliteOutputTests
{
    /// <summary>Verifies disposal removes an uncommitted database and every known sidecar.</summary>
    [Test]
    public async Task Dispose_DeletesUncommittedDatabaseAndSidecars()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = directory.GetPath("result.mnote");
        string stagingPath;

        using (var output = PendingSqliteOutput.Create(finalPath))
        {
            stagingPath = output.StagingPath;
            foreach (var path in SqliteDatabaseFiles.Enumerate(stagingPath))
                await File.WriteAllTextAsync(path, "incomplete");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(finalPath), Is.False);
            Assert.That(SqliteDatabaseFiles.AnyExists(stagingPath), Is.False);
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
        }
    }

    /// <summary>Verifies commit publishes only the completed main database.</summary>
    [Test]
    public async Task Commit_MovesOnlyCompletedDatabaseToFinalPath()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = directory.GetPath("result.mnote");

        using (var output = PendingSqliteOutput.Create(finalPath))
        {
            await File.WriteAllTextAsync(output.StagingPath, "complete");
            output.Commit();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(finalPath), Is.EqualTo("complete"));
            Assert.That(
                Directory.GetFileSystemEntries(directory.Path),
                Is.EqualTo(new[] { finalPath }));
        }
    }

    /// <summary>Verifies a destination created after staging is never overwritten.</summary>
    [Test]
    public async Task Commit_WithExistingFinalPath_PreservesExistingOutput()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = directory.GetPath("result.mnote");
        using var output = PendingSqliteOutput.Create(finalPath);
        await File.WriteAllTextAsync(output.StagingPath, "new");
        await File.WriteAllTextAsync(finalPath, "existing");

        Assert.That(output.Commit, Throws.TypeOf<IOException>());
        Assert.That(await File.ReadAllTextAsync(finalPath), Is.EqualTo("existing"));
    }

    /// <summary>Verifies an existing final sidecar is treated as an output conflict.</summary>
    [Test]
    public async Task Create_WithExistingFinalSidecar_PreservesIt()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = directory.GetPath("result.mnote");
        var sidecarPath = finalPath + "-wal";
        await File.WriteAllTextAsync(sidecarPath, "existing");

        Assert.That(
            () => PendingSqliteOutput.Create(finalPath),
            Throws.TypeOf<IOException>());
        Assert.That(await File.ReadAllTextAsync(sidecarPath), Is.EqualTo("existing"));
    }

    /// <summary>Verifies commit rejects an incomplete SQLite file set.</summary>
    [Test]
    public async Task Commit_WithWalSidecar_RejectsIncompleteFileSet()
    {
        using var directory = new TemporaryDirectory();
        var finalPath = directory.GetPath("result.mnote");
        using var output = PendingSqliteOutput.Create(finalPath);
        await File.WriteAllTextAsync(output.StagingPath, "database");
        await File.WriteAllTextAsync(output.StagingPath + "-wal", "pending");

        Assert.That(output.Commit, Throws.TypeOf<InvalidOperationException>());
        Assert.That(File.Exists(finalPath), Is.False);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
