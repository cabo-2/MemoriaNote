using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies non-mutating live notebook format validation.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SqliteNotebookFormatValidatorTests
{
    /// <summary>Verifies a current notebook can be validated and reopened.</summary>
    [Test]
    public async Task ValidateCurrentAsync_CurrentNotebook_ReturnsMetadata()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        var validator = CreateValidator(out _);

        var result = await validator.ValidateCurrentAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasIssues, Is.False);
            Assert.That(result.Metadata.Name, Is.EqualTo("work"));
            Assert.That(result.Metadata.Title, Is.EqualTo("Work"));
            Assert.That(
                result.Metadata.Version,
                Is.EqualTo(NotebookDbContext.CurrentVersion));
        }
    }

    /// <summary>Verifies an unsupported version is rejected without changing metadata.</summary>
    [Test]
    public async Task ValidateCurrentAsync_UnsupportedVersion_DoesNotMigrateOrUpdateFile()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        var validator = CreateValidator(out var metadataRepository);
        await metadataRepository.UpdateAsync(
            database.DatabasePath,
            new NotebookMetadataPatch().SetVersion("unsupported"),
            CancellationToken.None);

        Assert.ThrowsAsync<InvalidDataException>(new Func<Task>(async () =>
            await validator.ValidateCurrentAsync(
                database.DatabasePath,
                CancellationToken.None)));

        var metadata = await metadataRepository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);
        Assert.That(metadata.Metadata.Version, Is.EqualTo("unsupported"));
    }

    /// <summary>Verifies validating a missing path never creates a SQLite file.</summary>
    [Test]
    public void ValidateCurrentAsync_MissingNotebook_DoesNotCreateFile()
    {
        using var database = new TemporaryNotebookDatabase();
        var validator = CreateValidator(out _);

        Assert.ThrowsAsync<FileNotFoundException>(new Func<Task>(async () =>
            await validator.ValidateCurrentAsync(
                database.DatabasePath,
                CancellationToken.None)));

        Assert.That(File.Exists(database.DatabasePath), Is.False);
    }

    static SqliteNotebookFormatValidator CreateValidator(
        out INotebookMetadataRepository metadataRepository)
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        metadataRepository = new SqliteNotebookMetadataRepository(factory);
        return new SqliteNotebookFormatValidator(factory, metadataRepository);
    }
}
