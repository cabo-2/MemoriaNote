using System.Runtime.CompilerServices;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Integration.Validation;
using MemoriaNote.Persistence;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Verifies the public first-party notebook validation integration surface.</summary>
[TestFixture]
[Category("Functional")]
public sealed class NotebookValidationTests
{
    /// <summary>Verifies explicitly selected derived data can be checked and rebuilt.</summary>
    [Test]
    public async Task DerivedData_CheckAndRebuild_UsesExplicitSelection()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("derived.mnote");
        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(databasePath, "Derived"));
        await using var application = created.Value!;
        await application.Pages.CreateAsync(
            new NotebookPageCreateRequest("page", "searchable body", ["tag"]));
        await ExecuteAsync(
            databasePath,
            "DELETE FROM PageTags; " +
            "INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61) VALUES('delete-all'); " +
            "INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');");
        var beforeBytes = await File.ReadAllBytesAsync(databasePath);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);
        var validation = NotebookValidation.CreateSession(application);

        var selected = new[]
        {
            DerivedDataKind.Fts5Unicode61,
            DerivedDataKind.PageTags
        };
        var check = await validation.CheckAsync(selected);

        Assert.That(check.IsSuccess, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.Value!.IsValid, Is.False);
            Assert.That(
                check.Value.Selected,
                Is.EqualTo(new[]
                {
                    DerivedDataKind.PageTags,
                    DerivedDataKind.Fts5Unicode61
                }));
            Assert.That(
                check.Value.Issues.Select(issue => issue.DerivedData),
                Is.EqualTo(new[]
                {
                    DerivedDataKind.PageTags,
                    DerivedDataKind.Fts5Unicode61
                }));
            Assert.That(await File.ReadAllBytesAsync(databasePath), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(beforeFiles));
        }

        var rebuilt = await validation.RebuildAsync(selected);
        var selectedAfter = await validation.CheckAsync(selected);
        var trigramAfter = await validation.CheckAsync([DerivedDataKind.Fts5Trigram]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rebuilt.Value!.WasCommitted, Is.True);
            Assert.That(rebuilt.Value.Validation.IsValid, Is.True);
            Assert.That(selectedAfter.Value!.IsValid, Is.True);
            Assert.That(trigramAfter.Value!.IsValid, Is.False);
            Assert.That(
                trigramAfter.Value.Issues.Single().DerivedData,
                Is.EqualTo(DerivedDataKind.Fts5Trigram));
        }
    }

    /// <summary>Verifies unsupported selections are classified without touching the notebook.</summary>
    [Test]
    public async Task DerivedData_WithInvalidSelection_ReturnsClassifiedFailure()
    {
        using var directory = new TemporaryDirectory();
        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(directory.GetPath("selection.mnote"), "Selection"));
        await using var application = created.Value!;
        var validation = NotebookValidation.CreateSession(application);

        var empty = await validation.CheckAsync(Array.Empty<DerivedDataKind>());
        var unknown = await validation.RebuildAsync([(DerivedDataKind)99]);

        AssertFailure(empty, "derived.unsupported_selection");
        AssertFailure(unknown, "derived.unsupported_selection");
    }

    /// <summary>Verifies schema defects are reported as prerequisites and never rebuilt.</summary>
    [Test]
    public async Task DerivedData_WithSchemaDefect_RefusesMaintenance()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("schema-defect.mnote");
        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(databasePath, "Schema defect"));
        await using var application = created.Value!;
        var validation = NotebookValidation.CreateSession(application);
        await ExecuteAsync(databasePath, "DROP TABLE FtsIndexTrigram;");

        var result = await validation.RebuildAsync([DerivedDataKind.Fts5Trigram]);

        AssertFailure(result, "derived.prerequisite_invalid");
    }

    /// <summary>Verifies validation operations follow the owning application-session lifetime.</summary>
    [Test]
    public async Task DerivedData_AfterApplicationDispose_RejectsOperations()
    {
        using var directory = new TemporaryDirectory();
        var created = await NotebookApplication.CreateAsync(
            new NotebookCreateRequest(directory.GetPath("disposed.mnote"), "Disposed"));
        var application = created.Value!;
        var validation = NotebookValidation.CreateSession(application);
        await application.DisposeAsync();

        Assert.That(
            async () => await validation.CheckAsync([DerivedDataKind.PageTags]),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies deterministic publication preserves typed logical values.</summary>
    [Test]
    public async Task DeterministicPublisher_PublishesCanonicalStreamingCorpus()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("corpus.mnote");
        var pageId = PageId.FromGuid(Guid.Parse("11111111-2222-4333-8444-555555555555"));
        var metadata = new Dictionary<string, string?>
        {
            ["Author"] = string.Empty,
            ["CreateTime"] = null,
            ["Description"] = string.Empty,
            ["Extension"] = null,
            ["ReadOnly"] = "False",
            ["Tag"] = string.Empty,
            ["Title"] = "Deterministic"
        };
        var page = new DeterministicNotebookPage(
            pageId,
            "日本語",
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero),
            revision: 7,
            ["雪", "alpha", "alpha"],
            "first\nsecond");

        var published = await DeterministicNotebookPublisher.PublishAsync(
            new DeterministicNotebookPublicationRequest(
                destination,
                metadata,
                ToAsync([page])));

        Assert.That(published.IsSuccess, Is.True);
        var opened = await NotebookApplication.OpenAsync(new NotebookOpenRequest(destination));
        await using var application = opened.Value!;
        var loadedMetadata = await application.Metadata.GetAsync();
        var loadedPage = await application.Pages.GetByIdAsync(pageId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(published.Value!.FormatVersion, Is.EqualTo(2));
            Assert.That(loadedMetadata.Value!.Values["Extension"], Is.Null);
            Assert.That(loadedPage.Value!.Name, Is.EqualTo("日本語"));
            Assert.That(loadedPage.Value.Revision, Is.EqualTo(7));
            Assert.That(loadedPage.Value.Tags, Is.EqualTo(new[] { "alpha", "雪" }));
            Assert.That(loadedPage.Value.Text, Is.EqualTo("first\nsecond"));
        }
    }

    /// <summary>Verifies invalid corpus input is classified and leaves no output.</summary>
    [Test]
    public async Task DeterministicPublisher_WithDuplicateName_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("invalid.mnote");
        var first = CreatePage("11111111-1111-4111-8111-111111111111", "duplicate");
        var second = CreatePage("22222222-2222-4222-8222-222222222222", "duplicate");

        var result = await DeterministicNotebookPublisher.PublishAsync(
            new DeterministicNotebookPublicationRequest(
                destination,
                ValidMetadata(),
                ToAsync([first, second])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.Validation));
            Assert.That(result.Failure.Code, Is.EqualTo("corpus.publish.invalid_data"));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
        }
    }

    /// <summary>Verifies cancellation during streaming publication removes private output.</summary>
    [Test]
    public void DeterministicPublisher_WhenStreamCancels_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("cancelled.mnote");
        using var cancellation = new CancellationTokenSource();

        Assert.That(
            async () => await DeterministicNotebookPublisher.PublishAsync(
                new DeterministicNotebookPublicationRequest(
                    destination,
                    ValidMetadata(),
                    CancelingPages(cancellation)),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    private static DeterministicNotebookPage CreatePage(string pageId, string name) => new(
        PageId.FromGuid(Guid.Parse(pageId)),
        name,
        new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 1, 0, 0, 1, TimeSpan.Zero),
        1,
        [],
        "text");

    private static IReadOnlyDictionary<string, string?> ValidMetadata() =>
        new Dictionary<string, string?>
        {
            ["Author"] = string.Empty,
            ["CreateTime"] = null,
            ["Description"] = string.Empty,
            ["ReadOnly"] = "False",
            ["Tag"] = string.Empty,
            ["Title"] = "Corpus"
        };

    private static async IAsyncEnumerable<DeterministicNotebookPage> CancelingPages(
        CancellationTokenSource cancellation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return CreatePage("11111111-1111-4111-8111-111111111111", "first");
        cancellation.Cancel();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(
        IEnumerable<T> values,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
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

    private static void AssertFailure<T>(NotebookOperationResult<T> result, string code)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure!.Category, Is.EqualTo(NotebookFailureCategory.DerivedData));
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

        internal string GetPath(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
