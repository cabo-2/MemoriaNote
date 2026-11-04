using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
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
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();

        var first = await repository.UpdateAsync(
            notebookId,
            new NotebookMetadataChanges(new Dictionary<string, string>
            {
                ["Title"] = "After",
                ["name"] = "case-sensitive extension",
                ["Unknown"] = "preserved",
                ["NullableUnknown"] = null!
            }),
            CancellationToken.None);
        var loaded = await repository.LoadAsync(notebookId, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.NotebookId, Is.EqualTo(notebookId));
            Assert.That(loaded.Values["Title"], Is.EqualTo("After"));
            Assert.That(loaded.Values["ReadOnly"], Is.EqualTo("False"));
            Assert.That(loaded.Values["name"], Is.EqualTo("case-sensitive extension"));
            Assert.That(loaded.Values["Unknown"], Is.EqualTo("preserved"));
            Assert.That(loaded.Values["NullableUnknown"], Is.Null);
        }
    }

    /// <summary>Verifies retired keys reject the entire metadata change set.</summary>
    [TestCase("Name")]
    [TestCase("Version")]
    public async Task UpdateAsync_WhenRetiredKeyIsIncluded_RejectsEntireChangeSet(
        string retiredKey)
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("fixed-version.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();
        var changes = new NotebookMetadataChanges(new Dictionary<string, string>
        {
            ["Title"] = "Must not be saved",
            [retiredKey] = "retired"
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
            Assert.That(loaded.Values.ContainsKey(retiredKey), Is.False);
        }
    }

    /// <summary>Verifies managed values are validated before any write occurs.</summary>
    [TestCase("ReadOnly", "true")]
    [TestCase("CreateTime", "20261301120000")]
    public async Task UpdateAsync_WithInvalidManagedValue_RejectsEntireChangeSet(
        string key,
        string value)
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("invalid-managed.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();

        Assert.That(
            async () => await repository.UpdateAsync(
                notebookId,
                new NotebookMetadataChanges(new Dictionary<string, string>
                {
                    ["Title"] = "Must not be saved",
                    [key] = value
                }),
                CancellationToken.None),
            Throws.TypeOf<ArgumentException>());

        var loaded = await repository.LoadAsync(notebookId, CancellationToken.None);
        Assert.That(loaded.Values["Title"], Is.EqualTo("Before"));
    }

    /// <summary>Verifies nullable CreateTime and an empty extension key retain exact values.</summary>
    [Test]
    public async Task UpdateAsync_WithNullableCreateTimeAndEmptyExtension_PreservesValues()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("nullable.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Before");
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var repository = new SchemaV2MetadataRepository();

        var updated = await repository.UpdateAsync(
            notebookId,
            new NotebookMetadataChanges(new Dictionary<string, string>
            {
                ["CreateTime"] = null!,
                [string.Empty] = null!
            }),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Values["CreateTime"], Is.Null);
            Assert.That(updated.Values[string.Empty], Is.Null);
        }
        Assert.That(
            (await SchemaV2DatabaseValidator.ValidateAsync(databasePath)).IsValid,
            Is.True);
    }

    /// <summary>Verifies pre-cancellation performs no metadata write.</summary>
    [Test]
    public async Task UpdateAsync_WhenPreCanceled_DoesNotWrite()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Before");
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
