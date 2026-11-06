using MemoriaNote.Application;
using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies the public current-format notebook application surface.</summary>
[TestFixture]
[Category("Functional")]
public sealed class NotebookApplicationTests
{
    /// <summary>Verifies create, metadata, page CRUD, and reopen through only public operations.</summary>
    [Test]
    public async Task ApplicationSurface_ExecutesCanonicalNotebookWorkflow()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("application.mnote");

        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(databasePath, "Application notebook"));

        Assert.That(created.IsSuccess, Is.True);
        await using (var session = created.Value!)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(session.Notebook.FormatVersion, Is.EqualTo(2));
                Assert.That(session.Notebook.NotebookId.ToString(), Is.EqualTo(databasePath));
            }

            var metadata = await session.Metadata.UpdateAsync(
                new NotebookMetadataChangeSet(new Dictionary<string, string?>
                {
                    ["Title"] = "Updated",
                    ["Extension"] = null
                }));
            Assert.That(metadata.IsSuccess, Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata.Value!.Values["Title"], Is.EqualTo("Updated"));
                Assert.That(metadata.Value.Values["Extension"], Is.Null);
                Assert.That(metadata.Value.Values["ReadOnly"], Is.EqualTo("False"));
            }

            var invalidMetadata = await session.Metadata.UpdateAsync(
                new NotebookMetadataChangeSet(new Dictionary<string, string?>
                {
                    ["Name"] = "retired"
                }));
            AssertFailure(
                invalidMetadata,
                NotebookFailureCategory.Validation,
                "metadata.update.invalid_value");

            var first = await session.Pages.CreateAsync(
                new NotebookPageCreateRequest(
                    "first",
                    "first body",
                    ["日本語", "alpha", "alpha"]));
            var second = await session.Pages.CreateAsync(
                new NotebookPageCreateRequest("second", "second body", []));
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(first.Value!.Tags, Is.EqualTo(new[] { "alpha", "日本語" }));

            var duplicate = await session.Pages.CreateAsync(
                new NotebookPageCreateRequest("first", "duplicate", []));
            AssertFailure(
                duplicate,
                NotebookFailureCategory.Conflict,
                "page.name_conflict");

            var prefix = first.Value.PageId.ToString().Replace("-", string.Empty)[..8];
            var resolved = await session.Pages.ResolveByIdPrefixAsync(prefix);
            Assert.That(resolved.Value!.PageId, Is.EqualTo(first.Value.PageId));

            var replaced = await session.Pages.ReplaceAsync(
                first.Value.PageId,
                first.Value.Revision,
                new NotebookPageReplaceRequest("renamed", "updated body", ["reviewed"]));
            Assert.That(replaced.IsSuccess, Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(replaced.Value!.Revision, Is.EqualTo(2));
                Assert.That(replaced.Value.Name, Is.EqualTo("renamed"));
                Assert.That(replaced.Value.Text, Is.EqualTo("updated body"));
            }

            var stale = await session.Pages.DeleteAsync(
                first.Value.PageId,
                expectedRevision: 1);
            AssertFailure(
                stale,
                NotebookFailureCategory.Conflict,
                "page.revision_conflict");

            var summaries = await session.Pages.ListSummariesAsync(0, 10);
            Assert.That(
                summaries.Value!.Select(summary => summary.Name),
                Is.EqualTo(new[] { "renamed", "second" }));
            Assert.That(await session.Pages.CountAsync(), Has.Property("Value").EqualTo(2));

            var deleted = await session.Pages.DeleteAsync(
                replaced.Value.PageId,
                replaced.Value.Revision);
            Assert.That(deleted.Value, Is.True);
        }

        var reopened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(databasePath));
        Assert.That(reopened.IsSuccess, Is.True);
        await using var reopenedSession = reopened.Value!;
        var loadedMetadata = await reopenedSession.Metadata.GetAsync();
        var loadedPage = await reopenedSession.Pages.GetByNameAsync("second");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedMetadata.Value!.Values["Extension"], Is.Null);
            Assert.That(loadedPage.Value!.Text, Is.EqualTo("second body"));
            Assert.That((await reopenedSession.Pages.CountAsync()).Value, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies recognition and semantic validation return stable format failures.</summary>
    [Test]
    public async Task OpenAsync_ClassifiesMissingLegacyAndSchemaDefect()
    {
        using var directory = new TemporaryDirectory();
        var missing = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(directory.GetPath("missing.mnote")));
        AssertFailure(missing, NotebookFailureCategory.NotFound, "notebook.missing");

        using var legacy = new TemporaryNotebookDatabase();
        legacy.CreateNotebook("legacy", "Legacy");
        var legacyResult = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(legacy.DatabasePath));
        AssertFailure(legacyResult, NotebookFailureCategory.Format, "format.legacy");

        var invalidPath = directory.GetPath("invalid.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(invalidPath, "Invalid");
        await ExecuteAsync(invalidPath, "DROP INDEX IX_Pages_Name;");
        var invalid = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(invalidPath));
        AssertFailure(invalid, NotebookFailureCategory.Format, "format.schema_invalid");
    }

    /// <summary>Verifies derived content mismatch does not prevent authoritative operations.</summary>
    [Test]
    public async Task OpenAsync_WithDerivedMismatch_StillOpensApplicationSession()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("derived-mismatch.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Derived mismatch");
        await ExecuteAsync(
            databasePath,
            "INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text) " +
            "VALUES ('11111111-1111-1111-1111-111111111111', 'page', " +
            "'2026-11-01 00:00:00.0000000', '2026-11-01 00:00:00.0000000', 1, '[]', 'body'); " +
            "DELETE FROM FtsIndexUnicode61;");

        var result = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(databasePath));

        Assert.That(result.IsSuccess, Is.True);
        await using var session = result.Value!;
        Assert.That((await session.Pages.CountAsync()).Value, Is.EqualTo(1));
    }

    /// <summary>Verifies an existing destination is classified and remains unchanged.</summary>
    [Test]
    public async Task CreateAsync_WithExistingDestination_PreservesIt()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("existing.mnote");
        await File.WriteAllTextAsync(databasePath, "existing");

        var result = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(databasePath, "Replacement"));

        AssertFailure(
            result,
            NotebookFailureCategory.Conflict,
            "notebook.destination_exists");
        Assert.That(await File.ReadAllTextAsync(databasePath), Is.EqualTo("existing"));
    }

    /// <summary>Verifies caller cancellation propagates and leaves no output.</summary>
    [Test]
    public void CreateAsync_WhenPreCancelled_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await NotebookApplication.CreateAsync(
                new NotebookCreateRequest(databasePath, "Cancelled"),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies disposed sessions reject subsequent operations.</summary>
    [Test]
    public async Task Session_AfterDispose_RejectsOperations()
    {
        using var directory = new TemporaryDirectory();
        var result = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(directory.GetPath("disposed.mnote"), "Disposed"));
        var session = result.Value!;
        await session.DisposeAsync();

        Assert.That(
            async () => await session.Pages.CountAsync(),
            Throws.TypeOf<ObjectDisposedException>());
    }

    private static async Task ExecuteAsync(string databasePath, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadWrite);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertFailure<T>(
        NotebookOperationResult<T> result,
        NotebookFailureCategory category,
        string code)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Value, Is.EqualTo(default(T)));
            Assert.That(result.Failure!.Category, Is.EqualTo(category));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
        }
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
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
