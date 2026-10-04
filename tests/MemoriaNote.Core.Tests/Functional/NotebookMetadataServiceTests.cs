using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies the application metadata service against SQLite persistence.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class NotebookMetadataServiceTests
{
    /// <summary>Verifies a display name is updated without a duplicate-name constraint.</summary>
    [Test]
    public async Task UpdateAsync_Name_UpdatesDisplayName()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("original", "Original title");
        var repository = CreateRepository();
        var service = new NotebookMetadataService(repository);

        var result = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(database.DatabasePath),
                NotebookMetadataField.Name,
                "shared-name"),
            CancellationToken.None);

        var loaded = await repository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Changed, Is.True);
            Assert.That(result.Metadata.Name, Is.EqualTo("shared-name"));
            Assert.That(loaded.Metadata.Name, Is.EqualTo("shared-name"));
        }
    }

    /// <summary>Verifies setting the current value does not invoke persistence update.</summary>
    [Test]
    public async Task UpdateAsync_SameValue_ReturnsUnchangedWithoutUpdate()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("same", "Same title");
        var counting = new CountingMetadataRepository(CreateRepository());
        var service = new NotebookMetadataService(counting);

        var result = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(database.DatabasePath),
                NotebookMetadataField.Title,
                "Same title"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(counting.LoadCount, Is.EqualTo(1));
            Assert.That(counting.UpdateCount, Is.Zero);
        }
    }

    /// <summary>Verifies logical read-only does not block metadata or access updates.</summary>
    [Test]
    public async Task UpdateAsync_ReadOnlyNotebook_AllowsMetadataAndAccessChanges()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("read-only", "Original title");
        var repository = CreateRepository();
        await repository.UpdateAsync(
            database.DatabasePath,
            new NotebookMetadataPatch().SetReadOnly(true),
            CancellationToken.None);
        var service = new NotebookMetadataService(repository);

        var titleResult = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(database.DatabasePath),
                NotebookMetadataField.Title,
                "Updated while read-only"),
            CancellationToken.None);
        var accessResult = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(database.DatabasePath),
                NotebookMetadataField.ReadOnly,
                "false"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(titleResult.IsSuccess, Is.True);
            Assert.That(titleResult.Metadata.ReadOnly, Is.True);
            Assert.That(accessResult.IsSuccess, Is.True);
            Assert.That(accessResult.Metadata.ReadOnly, Is.False);
            Assert.That(accessResult.Metadata.Title, Is.EqualTo("Updated while read-only"));
        }
    }

    /// <summary>Verifies one update preserves application-managed and unknown rows.</summary>
    [Test]
    public async Task UpdateAsync_UserField_PreservesManagedAndUnknownRows()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("preserve", "Original title");
        using (var context = new NotebookDbContext(database.DatabasePath))
        {
            context.Metadata.AddRange(
                new NoteKeyValue
                {
                    Key = NoteKeyValue.CreateTime,
                    Value = "20260102030405"
                },
                new NoteKeyValue
                {
                    Key = "FutureKey",
                    Value = "future-value"
                });
            context.SaveChanges();
        }
        var repository = CreateRepository();
        var service = new NotebookMetadataService(repository);

        var result = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(database.DatabasePath),
                NotebookMetadataField.Description,
                string.Empty),
            CancellationToken.None);

        using var verification = new NotebookDbContext(database.DatabasePath);
        var values = verification.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(values[NoteKeyValue.Description], Is.EqualTo(string.Empty));
            Assert.That(values[NoteKeyValue.Version], Is.EqualTo(NotebookDbContext.CurrentVersion));
            Assert.That(values[NoteKeyValue.CreateTime], Is.EqualTo("20260102030405"));
            Assert.That(values["FutureKey"], Is.EqualTo("future-value"));
        }
    }

    /// <summary>Verifies optional author and tag fields are independently editable.</summary>
    [Test]
    public async Task UpdateAsync_OptionalFields_StoresEachExactValue()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("optional", "Optional fields");
        var service = new NotebookMetadataService(CreateRepository());
        var notebookId = NotebookId.FromDatabasePath(database.DatabasePath);

        await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                notebookId,
                NotebookMetadataField.Author,
                " Author "),
            CancellationToken.None);
        var result = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                notebookId,
                NotebookMetadataField.Tag,
                "tag-value"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Metadata.Author, Is.EqualTo(" Author "));
            Assert.That(result.Metadata.Tag, Is.EqualTo("tag-value"));
        }
    }

    /// <summary>Verifies invalid input returns typed errors without database access.</summary>
    [Test]
    public async Task UpdateAsync_InvalidValue_ReturnsValidationBeforeDatabaseAccess()
    {
        var repository = new CountingMetadataRepository(CreateRepository());
        var service = new NotebookMetadataService(repository);
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mnote");

        var result = await service.UpdateAsync(
            new NotebookMetadataUpdateRequest(
                NotebookId.FromDatabasePath(path),
                NotebookMetadataField.Name,
                "invalid\nname"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Is.EqualTo(new[]
            {
                NotebookMetadataErrorCode.ControlCharacter
            }));
            Assert.That(repository.LoadCount, Is.Zero);
            Assert.That(repository.UpdateCount, Is.Zero);
        }
    }

    static SqliteNotebookMetadataRepository CreateRepository()
    {
        return new SqliteNotebookMetadataRepository(
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance));
    }

    sealed class CountingMetadataRepository : INotebookMetadataRepository
    {
        readonly INotebookMetadataRepository _inner;

        internal CountingMetadataRepository(INotebookMetadataRepository inner)
        {
            _inner = inner;
        }

        internal int LoadCount { get; private set; }

        internal int UpdateCount { get; private set; }

        public Task<NotebookMetadataResult> LoadAsync(
            string databasePath,
            CancellationToken token)
        {
            LoadCount++;
            return _inner.LoadAsync(databasePath, token);
        }

        public Task<NotebookMetadataResult> UpdateAsync(
            string databasePath,
            NotebookMetadataPatch patch,
            CancellationToken token)
        {
            UpdateCount++;
            return _inner.UpdateAsync(databasePath, patch, token);
        }
    }
}
