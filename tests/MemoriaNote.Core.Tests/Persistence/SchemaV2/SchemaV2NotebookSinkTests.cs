using System.Runtime.CompilerServices;
using MemoriaNote.Application;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the shared current-record to Schema V2 publication boundary.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2NotebookSinkTests
{
    /// <summary>Verifies exact authoritative values and rebuilt derived data are published.</summary>
    [Test]
    public async Task PublishAsync_PreservesLogicalValuesAndValidatesDatabase()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("restored.mnote");
        var metadata = new[]
        {
            new CurrentNotebookMetadataRecord("Author", string.Empty),
            new CurrentNotebookMetadataRecord("CreateTime", null),
            new CurrentNotebookMetadataRecord("Description", string.Empty),
            new CurrentNotebookMetadataRecord("NullValue", null),
            new CurrentNotebookMetadataRecord("ReadOnly", "False"),
            new CurrentNotebookMetadataRecord("Tag", string.Empty),
            new CurrentNotebookMetadataRecord("Title", "Sink notebook")
        };
        var pages = new[]
        {
            new CurrentNotebookPageRecord(
                "11111111-2222-4333-8444-555555555555",
                "日本語",
                "2026-11-01 00:00:00.0000000",
                "2026-11-01 00:00:01.0000000",
                7,
                "[\"alpha\",\"雪\"]",
                "first\nsecond")
        };

        var result = await SchemaV2NotebookSink.PublishAsync(
            destination,
            new CurrentNotebookTransfer(metadata, ToAsync(pages)),
            Array.Empty<string>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Validation.IsValid, Is.True);
            Assert.That(result.Fingerprint.MetadataCount, Is.EqualTo(7));
            Assert.That(result.Fingerprint.PageCount, Is.EqualTo(1));
            Assert.That(File.Exists(destination), Is.True);
            Assert.That(SqliteDatabaseFiles.Enumerate(destination).Skip(1), Has.None.Matches<string>(File.Exists));
        }

        await using var connection = await SqliteConnectionFactory.OpenAsync(
            destination,
            SqliteOpenMode.ReadOnly);
        Assert.That(await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM PageTags;"), Is.EqualTo(2));
        Assert.That(
            await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM FtsIndexTrigram('second');"),
            Is.EqualTo(1));
        Assert.That(
            await ScalarAsync<string?>(
                connection,
                "SELECT Value FROM Metadata WHERE Key = 'NullValue';"),
            Is.Null);
    }

    /// <summary>Verifies a source/destination conflict is rejected without creating output.</summary>
    [Test]
    public void PublishAsync_WithSourceConflict_LeavesSourceUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.GetPath("source.mnote");
        File.WriteAllText(path, "source");
        var transfer = new CurrentNotebookTransfer(
            Array.Empty<CurrentNotebookMetadataRecord>(),
            ToAsync(Array.Empty<CurrentNotebookPageRecord>()));

        Assert.That(
            async () => await SchemaV2NotebookSink.PublishAsync(
                path,
                transfer,
                new[] { path }),
            Throws.TypeOf<IOException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo("source"));
    }

    /// <summary>Verifies an invalid logical input rolls back and removes its private file set.</summary>
    [Test]
    public void PublishAsync_WithInvalidValues_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("invalid.mnote");
        var transfer = new CurrentNotebookTransfer(
            new[]
            {
                new CurrentNotebookMetadataRecord("Author", string.Empty),
                new CurrentNotebookMetadataRecord("CreateTime", null),
                new CurrentNotebookMetadataRecord("Description", string.Empty),
                new CurrentNotebookMetadataRecord("ReadOnly", "False"),
                new CurrentNotebookMetadataRecord("Tag", string.Empty),
                new CurrentNotebookMetadataRecord("Title", "Invalid")
            },
            ToAsync(new[]
            {
                new CurrentNotebookPageRecord(
                    "00000000-0000-0000-0000-000000000000",
                    "page",
                    "2026-11-01 00:00:00.0000000",
                    "2026-11-01 00:00:00.0000000",
                    1,
                    "[]",
                    "text")
            }));

        Assert.That(
            async () => await SchemaV2NotebookSink.PublishAsync(
                destination,
                transfer,
                Array.Empty<string>()),
            Throws.InstanceOf<SqliteException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies retired metadata is rejected before private output is created.</summary>
    [Test]
    public void PublishAsync_WithRetiredMetadata_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("retired.mnote");
        var metadata = ValidMetadata()
            .Append(new CurrentNotebookMetadataRecord("Version", "2"))
            .ToArray();

        Assert.That(
            async () => await SchemaV2NotebookSink.PublishAsync(
                destination,
                new CurrentNotebookTransfer(
                    metadata,
                    ToAsync(Array.Empty<CurrentNotebookPageRecord>())),
                Array.Empty<string>()),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("retired"));
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies post-load validation failure removes the database and every sidecar.</summary>
    [Test]
    public void PublishAsync_WithPostValidationFailure_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("post-validation.mnote");
        var transfer = ValidEmptyTransfer();
        var invalidLayer = SchemaV2ValidationLayerResult.Evaluated(new[] { "injected failure" });
        var validLayer = SchemaV2ValidationLayerResult.Evaluated(Array.Empty<string>());
        var invalid = new SchemaV2DatabaseValidation(
            validLayer,
            invalidLayer,
            validLayer,
            validLayer,
            SchemaV2DerivedDataValidation.NotEvaluated);

        Assert.That(
            async () => await SchemaV2NotebookSink.PublishAsync(
                destination,
                transfer,
                Array.Empty<string>(),
                (_, _) => Task.FromResult(invalid)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("injected failure"));
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies cancellation during page loading rolls back and removes private output.</summary>
    [Test]
    public void PublishAsync_WithMidStreamCancellation_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = directory.GetPath("cancelled.mnote");
        using var cancellation = new CancellationTokenSource();
        var transfer = new CurrentNotebookTransfer(
            ValidMetadata(),
            CancelingPages(cancellation));

        Assert.That(
            async () => await SchemaV2NotebookSink.PublishAsync(
                destination,
                transfer,
                Array.Empty<string>(),
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    private static CurrentNotebookTransfer ValidEmptyTransfer() =>
        new(ValidMetadata(), ToAsync(Array.Empty<CurrentNotebookPageRecord>()));

    private static IReadOnlyList<CurrentNotebookMetadataRecord> ValidMetadata() =>
        new[]
        {
            new CurrentNotebookMetadataRecord("Author", string.Empty),
            new CurrentNotebookMetadataRecord("CreateTime", null),
            new CurrentNotebookMetadataRecord("Description", string.Empty),
            new CurrentNotebookMetadataRecord("ReadOnly", "False"),
            new CurrentNotebookMetadataRecord("Tag", string.Empty),
            new CurrentNotebookMetadataRecord("Title", "Sink")
        };

    private static async IAsyncEnumerable<CurrentNotebookPageRecord> CancelingPages(
        CancellationTokenSource cancellation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new CurrentNotebookPageRecord(
            "11111111-2222-4333-8444-555555555555",
            "first",
            "2026-11-01 00:00:00.0000000",
            "2026-11-01 00:00:00.0000000",
            1,
            "[]",
            "text");
        cancellation.Cancel();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<T?> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? default : (T)result;
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
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
