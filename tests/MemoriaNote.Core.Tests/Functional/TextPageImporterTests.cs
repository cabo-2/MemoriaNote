using System.Text;
using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies the flat, transactional text import contract.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class TextPageImporterTests
{
    /// <summary>Verifies only root lowercase text files are imported as strict UTF-8.</summary>
    [Test]
    public async Task Import_RootLowercaseTextFiles_PreservesTextAndRemovesBom()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var source = CreateSourceDirectory(database);
        var expected = "Café\r\n日本語\n";
        await File.WriteAllBytesAsync(
            Path.Combine(source, "Entry.txt"),
            Encoding.UTF8.Preamble.ToArray()
                .Concat(Encoding.UTF8.GetBytes(expected))
                .ToArray());
        await File.WriteAllTextAsync(Path.Combine(source, "Ignored.TXT"), "uppercase");
        var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "Ignored.txt"), "nested");
        var importer = CreateImporter();

        var result = await importer.ImportAsync(
            Request(notebook, source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.CreatedCount, Is.EqualTo(1));
            Assert.That(result.ReplacedCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.Zero);
            Assert.That(notebook.CountPages(), Is.EqualTo(1));
            Assert.That(notebook.ReadPage("Entry", 1).Text, Is.EqualTo(expected));
            Assert.That(notebook.ReadPage("Ignored", 1), Is.Null);
        }
    }

    /// <summary>Verifies invalid UTF-8 fails before any page is written.</summary>
    [Test]
    public async Task Import_InvalidUtf8_ReturnsValidationFailureWithoutWrites()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Before.txt"), "valid");
        await File.WriteAllBytesAsync(
            Path.Combine(source, "Invalid.txt"),
            new byte[] { 0x66, 0x80, 0x67 });

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(TextPageImportErrorCode.InvalidEncoding));
            Assert.That(notebook.CountPages(), Is.Zero);
        }
    }

    /// <summary>Verifies legacy and current encoded names cannot create one page twice.</summary>
    [Test]
    public async Task Import_MultipleFilesDecodeToSameName_ReturnsConflictWithoutWrites()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "~mn~1~A%2FB.txt"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(source, "A%2FB~mn~2~.txt"), "current");

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageImportErrorCode.DuplicateInputName));
            Assert.That(notebook.CountPages(), Is.Zero);
        }
    }

    /// <summary>Verifies fail rolls back planned creates when a later name conflicts.</summary>
    [Test]
    public async Task Import_FailPolicyExistingName_LeavesNotebookUnchanged()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var existing = notebook.CreatePage("Z existing", "original");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "A new.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "Z existing.txt"), "replacement");

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageImportErrorCode.ExistingPageConflict));
            Assert.That(notebook.CountPages(), Is.EqualTo(1));
            Assert.That(notebook.ReadPage("A new", 1), Is.Null);
            Assert.That(notebook.ReadPage(existing.Guid).Text, Is.EqualTo("original"));
        }
    }

    /// <summary>Verifies skip creates new pages while leaving an existing group unchanged.</summary>
    [Test]
    public async Task Import_SkipPolicy_ReportsCreatedAndSkippedCounts()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var existing = notebook.CreatePage("Existing", "original");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Existing.txt"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(source, "New.txt"), "created");

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source, TextPageImportConflictPolicy.Skip),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.CreatedCount, Is.EqualTo(1));
            Assert.That(result.ReplacedCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.EqualTo(1));
            Assert.That(notebook.ReadPage(existing.Guid).Text, Is.EqualTo("original"));
            Assert.That(notebook.ReadPage("New", 1).Text, Is.EqualTo("created"));
        }
    }

    /// <summary>Verifies replace changes only the body and modification timestamp.</summary>
    [Test]
    public async Task Import_ReplacePolicy_PreservesExistingPageIdentityAndMetadata()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        var existing = notebook.CreatePage("Existing", "original", "saved/path");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Existing.txt"), "replacement");
        var changedAt = new DateTimeOffset(2026, 10, 3, 1, 2, 3, TimeSpan.Zero);

        var result = await CreateImporter(changedAt).ImportAsync(
            Request(notebook, source, TextPageImportConflictPolicy.Replace),
            CancellationToken.None);
        var replaced = notebook.ReadPage(existing.Guid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.CreatedCount, Is.Zero);
            Assert.That(result.ReplacedCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.Zero);
            Assert.That(replaced.Guid, Is.EqualTo(existing.Guid));
            Assert.That(replaced.Name, Is.EqualTo(existing.Name));
            Assert.That(replaced.Index, Is.EqualTo(existing.Index));
            Assert.That(replaced.TagDict, Is.EqualTo(existing.TagDict));
            Assert.That(replaced.CreateTime, Is.EqualTo(existing.CreateTime));
            Assert.That(replaced.ContentType, Is.EqualTo(existing.ContentType));
            Assert.That(replaced.IsErased, Is.EqualTo(existing.IsErased));
            Assert.That(replaced.Text, Is.EqualTo("replacement"));
            Assert.That(replaced.UpdateTime, Is.EqualTo(changedAt.UtcDateTime));
        }
    }

    /// <summary>Verifies replace rejects a legacy duplicate-name group.</summary>
    [Test]
    public async Task Import_ReplacePolicyWithDuplicateExistingNames_ReturnsConflict()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        notebook.CreatePage("Existing", "first");
        notebook.CreatePage("Existing", "second");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Existing.txt"), "replacement");

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source, TextPageImportConflictPolicy.Replace),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageImportErrorCode.AmbiguousPageConflict));
            Assert.That(
                notebook.ReadPage("Existing").Select(page => page.Text),
                Is.EqualTo(new[] { "first", "second" }));
        }
    }

    /// <summary>Verifies dry-run reports planned work without changing the notebook.</summary>
    [Test]
    public async Task Import_DryRun_ReturnsCountsWithoutWrites()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        notebook.CreatePage("Existing", "original");
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Existing.txt"), "replacement");
        await File.WriteAllTextAsync(Path.Combine(source, "New.txt"), "created");

        var result = await CreateImporter().ImportAsync(
            new TextPageImportRequest(
                GetNotebookId(notebook),
                source,
                TextPageImportConflictPolicy.Replace,
                dryRun: true),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.CreatedCount, Is.EqualTo(1));
            Assert.That(result.ReplacedCount, Is.EqualTo(1));
            Assert.That(notebook.CountPages(), Is.EqualTo(1));
            Assert.That(notebook.ReadPage("Existing", 1).Text, Is.EqualTo("original"));
            Assert.That(notebook.ReadPage("New", 1), Is.Null);
        }
    }

    /// <summary>Verifies metadata read-only state is rechecked in the import transaction.</summary>
    [Test]
    public async Task Import_ReadOnlyNotebook_ReturnsConflictWithoutWrites()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("target", "Target");
        notebook.UpdateMetadata(new NotebookMetadataPatch().SetReadOnly(true));
        var source = CreateSourceDirectory(database);
        await File.WriteAllTextAsync(Path.Combine(source, "Entry.txt"), "text");

        var result = await CreateImporter().ImportAsync(
            Request(notebook, source),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(TextPageImportErrorCode.ReadOnly));
            Assert.That(notebook.CountPages(), Is.Zero);
        }
    }

    static string CreateSourceDirectory(TemporaryNotebookDatabase database)
    {
        var source = Path.Combine(database.DirectoryPath, "import");
        Directory.CreateDirectory(source);
        return source;
    }

    static TextPageImporter CreateImporter(DateTimeOffset? utcNow = null)
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        var repository = new SqliteNotebookTransferRepository(
            factory,
            new FixedClock(utcNow ?? new DateTimeOffset(
                2026,
                10,
                3,
                0,
                0,
                0,
                TimeSpan.Zero)));
        return new TextPageImporter(repository);
    }

    static TextPageImportRequest Request(
        Notebook notebook,
        string source,
        TextPageImportConflictPolicy policy = TextPageImportConflictPolicy.Fail)
    {
        return new TextPageImportRequest(GetNotebookId(notebook), source, policy);
    }

    static NotebookId GetNotebookId(Notebook notebook)
    {
        return NotebookId.FromDatabasePath(notebook.DatabasePath);
    }
}
