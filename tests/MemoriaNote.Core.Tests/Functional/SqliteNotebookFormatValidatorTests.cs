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

    /// <summary>Verifies a newer version is classified without changing metadata.</summary>
    [Test]
    public async Task ValidateCurrentAsync_NewerVersion_ReportsUnsupportedWithoutChanges()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        var validator = CreateValidator(out var metadataRepository);
        await metadataRepository.UpdateAsync(
            database.DatabasePath,
            new NotebookMetadataPatch().SetVersion("2"),
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<UnsupportedNotebookFormatVersionException>(
            new Func<Task>(async () =>
            await validator.ValidateCurrentAsync(
                database.DatabasePath,
                CancellationToken.None)));

        var metadata = await metadataRepository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.FormatVersion, Is.EqualTo("2"));
            Assert.That(metadata.Metadata.Version, Is.EqualTo("2"));
        }
    }

    /// <summary>Verifies an older or unrecognized version remains invalid.</summary>
    [TestCase("0")]
    [TestCase("old-version")]
    public async Task ValidateCurrentAsync_OldOrUnrecognizedVersion_IsInvalid(
        string version)
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("work", "Work");
        var validator = CreateValidator(out var metadataRepository);
        await metadataRepository.UpdateAsync(
            database.DatabasePath,
            new NotebookMetadataPatch().SetVersion(version),
            CancellationToken.None);

        Assert.ThrowsAsync<InvalidDataException>(new Func<Task>(async () =>
            await validator.ValidateCurrentAsync(
                database.DatabasePath,
                CancellationToken.None)));
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
