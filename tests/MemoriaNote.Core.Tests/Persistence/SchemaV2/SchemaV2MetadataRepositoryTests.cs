using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.SchemaV2;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies direct-SQL Schema V2 metadata persistence.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2MetadataRepositoryTests
{
    /// <summary>Verifies updates preserve unknown and unspecified metadata values.</summary>
    [Test]
    public async Task UpdateAsync_PreservesUnknownAndUnspecifiedValues()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("metadata.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "metadata", "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();

        var first = await repository.UpdateAsync(
            notebookId,
            new NotebookMetadataChanges(new Dictionary<string, string>
            {
                ["Title"] = "After",
                ["Unknown"] = "preserved",
                ["NullableUnknown"] = null!
            }),
            CancellationToken.None);
        var loaded = await repository.LoadAsync(notebookId, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.NotebookId, Is.EqualTo(notebookId));
            Assert.That(loaded.Values["Name"], Is.EqualTo("metadata"));
            Assert.That(loaded.Values["Title"], Is.EqualTo("After"));
            Assert.That(loaded.Values["Version"], Is.EqualTo("2"));
            Assert.That(loaded.Values["Unknown"], Is.EqualTo("preserved"));
            Assert.That(loaded.Values["NullableUnknown"], Is.Null);
        }
    }

    /// <summary>Verifies the format identity cannot be changed through metadata updates.</summary>
    [Test]
    public async Task UpdateAsync_WhenVersionIsIncluded_RejectsEntireChangeSet()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("fixed-version.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "fixed", "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();
        var changes = new NotebookMetadataChanges(new Dictionary<string, string>
        {
            ["Title"] = "Must not be saved",
            ["Version"] = "3"
        });

        Assert.That(
            async () => await repository.UpdateAsync(
                notebookId,
                changes,
                CancellationToken.None),
            Throws.TypeOf<ArgumentException>());

        var loaded = await repository.LoadAsync(notebookId, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Values["Title"], Is.EqualTo("Before"));
            Assert.That(loaded.Values["Version"], Is.EqualTo("2"));
        }
    }

    /// <summary>Verifies pre-cancellation performs no metadata write.</summary>
    [Test]
    public async Task UpdateAsync_WhenPreCanceled_DoesNotWrite()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "cancelled", "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await repository.UpdateAsync(
                notebookId,
                new NotebookMetadataChanges(new Dictionary<string, string>
                {
                    ["Title"] = "After"
                }),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        var loaded = await repository.LoadAsync(notebookId, CancellationToken.None);
        Assert.That(loaded.Values["Title"], Is.EqualTo("Before"));
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
