using System.Text;
using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>Verifies safe flat text export behavior.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class TextPageExporterTests
{
    /// <summary>Verifies text is flat, byte-order-mark free, and otherwise unchanged.</summary>
    [Test]
    public async Task Export_FlatUtf8Output_PreservesEveryPageBody()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("source", "Source");
        notebook.CreatePage("Root", "Café\r\n日本語\n");
        notebook.CreatePage("Nested", "line one\rline two", "ignored/path");
        var destination = Path.Combine(database.DirectoryPath, "export");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(notebook, destination),
            CancellationToken.None);

        var rootBytes = await File.ReadAllBytesAsync(
            Path.Combine(destination, "Root.txt"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ExportedCount, Is.EqualTo(2));
            Assert.That(Directory.GetDirectories(destination), Is.Empty);
            Assert.That(rootBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), Is.False);
            Assert.That(
                Encoding.UTF8.GetString(rootBytes),
                Is.EqualTo("Café\r\n日本語\n"));
            Assert.That(
                await File.ReadAllTextAsync(Path.Combine(destination, "Nested.txt")),
                Is.EqualTo("line one\rline two"));
        }
    }

    /// <summary>Verifies an empty notebook still publishes a new empty directory.</summary>
    [Test]
    public async Task Export_EmptyNotebook_PublishesEmptyDirectory()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("empty", "Empty");
        var destination = Path.Combine(database.DirectoryPath, "empty-export");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(notebook, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ExportedCount, Is.Zero);
            Assert.That(Directory.Exists(destination), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(destination), Is.Empty);
        }
    }

    /// <summary>Verifies an existing destination directory is never merged or changed.</summary>
    [Test]
    public async Task Export_ExistingDirectory_ReturnsConflictAndPreservesContents()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("source", "Source");
        notebook.CreatePage("Entry", "new text");
        var destination = Path.Combine(database.DirectoryPath, "existing");
        Directory.CreateDirectory(destination);
        var existingPath = Path.Combine(destination, "keep.txt");
        await File.WriteAllTextAsync(existingPath, "keep this text");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(notebook, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageExportErrorCode.DestinationConflict));
            Assert.That(await File.ReadAllTextAsync(existingPath), Is.EqualTo("keep this text"));
            Assert.That(Directory.GetFiles(destination), Has.Length.EqualTo(1));
        }
    }

    /// <summary>Verifies an existing destination file is never replaced.</summary>
    [Test]
    public async Task Export_ExistingFile_ReturnsConflictAndPreservesContents()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("source", "Source");
        notebook.CreatePage("Entry", "new text");
        var destination = Path.Combine(database.DirectoryPath, "existing");
        await File.WriteAllTextAsync(destination, "keep this file");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(notebook, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(TextPageExportErrorCode.DestinationConflict));
            Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("keep this file"));
        }
    }

    /// <summary>Verifies every member of a duplicate-name group gets a stable full-ID suffix.</summary>
    [Test]
    public async Task Export_IdSuffixPolicy_SuffixesEveryConflictingPage()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("source", "Source");
        var first = notebook.CreatePage("Plan/2026", "first");
        var second = notebook.CreatePage("Plan/2026", "second");
        notebook.CreatePage("Solo", "solo");
        var destination = Path.Combine(database.DirectoryPath, "suffixed");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(
                notebook,
                destination,
                TextPageExportNameConflictPolicy.IdSuffix),
            CancellationToken.None);

        var firstName = "Plan%2F2026~mn~2~~id~" +
            first.Guid.ToString("D").ToLowerInvariant() + ".txt";
        var secondName = "Plan%2F2026~mn~2~~id~" +
            second.Guid.ToString("D").ToLowerInvariant() + ".txt";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ExportedCount, Is.EqualTo(3));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, firstName)), Is.EqualTo("first"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, secondName)), Is.EqualTo("second"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "Solo.txt")), Is.EqualTo("solo"));
            Assert.That(
                File.Exists(Path.Combine(destination, "Plan%2F2026~mn~2~.txt")),
                Is.False);
        }
    }

    /// <summary>Verifies path-length conflicts fail before publishing any destination.</summary>
    [Test]
    public async Task Export_PathComponentTooLong_ReturnsNameConflictWithoutArtifacts()
    {
        using var database = new TemporaryNotebookDatabase();
        var notebook = database.CreateNotebook("source", "Source");
        notebook.CreatePage(new string('a', 300), "text");
        var destination = Path.Combine(database.DirectoryPath, "too-long");

        var result = await CreateExporter().ExportAsync(
            CreateRequest(notebook, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(TextPageExportErrorCode.NameConflict));
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(
                Directory.EnumerateDirectories(database.DirectoryPath, ".mn-export-*"),
                Is.Empty);
        }
    }

    static TextPageExportRequest CreateRequest(
        Notebook notebook,
        string destination,
        TextPageExportNameConflictPolicy policy = TextPageExportNameConflictPolicy.Fail)
    {
        return new TextPageExportRequest(
            NotebookId.FromDatabasePath(notebook.DatabasePath),
            destination,
            policy);
    }

    static TextPageExporter CreateExporter()
    {
        var factory = new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        return new TextPageExporter(new SqliteNotebookTransferRepository(factory));
    }
}
