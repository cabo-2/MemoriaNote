using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MemoriaNote.Archive;
using MemoriaNote.Persistence;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.NotebookMigration;

internal static class MigrationSourceFactory
{
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    internal static async Task<ILegacyNotebookMigrationSource> OpenAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InputNotFound,
                "The migration source does not exist.");
        }

        byte[] header;
        try
        {
            header = new byte[SqliteHeader.Length];
            await using var input = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                header.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var read = await input.ReadAsync(header, cancellationToken).ConfigureAwait(false);
            if (read < 2)
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.UnsupportedSource,
                    "The migration source is not a supported file.");
            }
            if (read < header.Length)
                Array.Resize(ref header, read);
        }
        catch (LegacySourceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.IoFailure,
                "The migration source could not be read.",
                exception);
        }

        if (header.AsSpan().SequenceEqual(SqliteHeader))
            return await SqliteSchemaV1MigrationSource.OpenAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
        if (header[0] == (byte)'P' && header[1] == (byte)'K')
            return await ArchiveContentModel1MigrationSource.OpenAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);

        throw new LegacySourceException(
            NotebookMigrationErrorCode.UnsupportedSource,
            "The migration source is neither a schema v1 notebook nor a CM1 archive.");
    }
}

internal sealed class SqliteSchemaV1MigrationSource : ILegacyNotebookMigrationSource
{
    private static readonly string[] ExpectedMigrations =
    [
        "20230404004629_InitialCreate",
        "20230404004813_FtsIndexTable"
    ];

    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;

    private SqliteSchemaV1MigrationSource(
        string sourcePath,
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<LegacyMetadataRecord> metadata)
    {
        SourcePath = sourcePath;
        _connection = connection;
        _transaction = transaction;
        Metadata = metadata;
    }

    public NotebookMigrationSourceKind Kind => NotebookMigrationSourceKind.SchemaV1Notebook;

    public string SourcePath { get; }

    public IReadOnlyList<LegacyMetadataRecord> Metadata { get; }

    internal static async Task<SqliteSchemaV1MigrationSource> OpenAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = null;
        SqliteTransaction transaction = null;
        try
        {
            connection = await SqliteConnectionFactory.OpenAsync(
                    sourcePath,
                    SqliteOpenMode.ReadOnly,
                    cancellationToken)
                .ConfigureAwait(false);
            transaction = connection.BeginTransaction(deferred: true);
            await ValidateIdentityAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            await ValidateIntegrityAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            var metadata = await ReadMetadataAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            return new SqliteSchemaV1MigrationSource(
                sourcePath,
                connection,
                transaction,
                metadata);
        }
        catch (LegacySourceException)
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (SqliteException exception)
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw new LegacySourceException(
                IsBusy(exception)
                    ? NotebookMigrationErrorCode.SourceBusy
                    : NotebookMigrationErrorCode.InvalidSource,
                "The schema v1 notebook could not provide a valid read snapshot.",
                exception);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async IAsyncEnumerable<LegacyPageRecord> ReadPagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            SELECT Rowid, Uuid, Name, "Index", Tags, ContentType,
                   CreateTime, UpdateTime, IsErased, Text
            FROM Pages
            ORDER BY Rowid;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var errors = new List<string>();
            var rowId = ReadInteger(reader, 0, "rowid", errors) ??
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "A schema v1 page has no usable source row identity.");
            yield return new LegacyPageRecord(
                rowId,
                ReadText(reader, 1, "uuid", nullable: false, errors),
                ReadText(reader, 2, "name", nullable: true, errors),
                ReadInteger(reader, 3, "index", errors) ?? 0,
                ReadText(reader, 4, "tags", nullable: true, errors),
                ReadText(reader, 5, "contentType", nullable: true, errors),
                ReadText(reader, 6, "createTime", nullable: false, errors),
                ReadText(reader, 7, "updateTime", nullable: false, errors),
                ReadInteger(reader, 8, "isErased", errors) ?? -1,
                ReadText(reader, 9, "text", nullable: true, errors),
                errors.AsReadOnly());
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task ValidateIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var version = await ScalarTextAsync(
                connection,
                transaction,
                "SELECT Value FROM Metadata WHERE Key = 'Version';",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(version, "1", StringComparison.Ordinal))
            Invalid("The SQLite source is not notebook format version 1.");

        await using var migrations = connection.CreateCommand();
        migrations.Transaction = transaction;
        migrations.CommandText = """
            SELECT MigrationId
            FROM __EFMigrationsHistory
            ORDER BY MigrationId COLLATE BINARY;
            """;
        var actual = new List<string>();
        await using (var reader = await migrations.ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                actual.Add(reader.GetString(0));
        }
        if (!actual.SequenceEqual(ExpectedMigrations, StringComparer.Ordinal))
            Invalid("The SQLite source does not have the complete schema v1 migration identity.");

        var required = new HashSet<string>(
            [
                "Rowid", "Uuid", "Name", "Index", "Tags", "ContentType",
                "CreateTime", "UpdateTime", "IsErased", "Text"
            ],
            StringComparer.OrdinalIgnoreCase);
        await using var columns = connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText = "PRAGMA table_info('Pages');";
        await using (var reader = await columns.ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                required.Remove(reader.GetString(1));
        }
        if (required.Count > 0)
            Invalid("The SQLite source is missing required schema v1 page columns.");
    }

    private static async Task ValidateIntegrityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var integrity = await ScalarTextAsync(
                connection,
                transaction,
                "PRAGMA integrity_check;",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            Invalid("The schema v1 notebook failed SQLite integrity_check.");
    }

    private static async Task<IReadOnlyList<LegacyMetadataRecord>> ReadMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key COLLATE BINARY;";
        var rows = new List<LegacyMetadataRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetFieldType(0) != typeof(string) ||
                (!reader.IsDBNull(1) && reader.GetFieldType(1) != typeof(string)))
            {
                Invalid("The schema v1 metadata contains a non-text value.");
            }
            rows.Add(new LegacyMetadataRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return rows.AsReadOnly();
    }

    private static async Task<string> ScalarTextAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static long? ReadInteger(
        SqliteDataReader reader,
        int ordinal,
        string field,
        ICollection<string> errors)
    {
        if (reader.IsDBNull(ordinal) || reader.GetFieldType(ordinal) != typeof(long))
        {
            errors.Add(field);
            return null;
        }
        return reader.GetInt64(ordinal);
    }

    private static string ReadText(
        SqliteDataReader reader,
        int ordinal,
        string field,
        bool nullable,
        ICollection<string> errors)
    {
        if (reader.IsDBNull(ordinal))
        {
            if (!nullable)
                errors.Add(field);
            return null;
        }
        if (reader.GetFieldType(ordinal) != typeof(string))
        {
            errors.Add(field);
            return null;
        }
        return reader.GetString(ordinal);
    }

    private static bool IsBusy(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6;

    private static void Invalid(string message) =>
        throw new LegacySourceException(NotebookMigrationErrorCode.InvalidSource, message);
}

internal sealed class ArchiveContentModel1MigrationSource : ILegacyNotebookMigrationSource
{
    private readonly FileStream _stream;
    private readonly ArchiveV1Reader _reader;

    private ArchiveContentModel1MigrationSource(
        string sourcePath,
        FileStream stream,
        ArchiveV1Reader reader,
        IReadOnlyList<LegacyMetadataRecord> metadata)
    {
        SourcePath = sourcePath;
        _stream = stream;
        _reader = reader;
        Metadata = metadata;
    }

    public NotebookMigrationSourceKind Kind => NotebookMigrationSourceKind.ContentModel1Archive;

    public string SourcePath { get; }

    public IReadOnlyList<LegacyMetadataRecord> Metadata { get; }

    internal static async Task<ArchiveContentModel1MigrationSource> OpenAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        FileStream stream = null;
        try
        {
            stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var reader = new ArchiveV1Reader();
            var validation = await reader.ValidateAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new LegacySourceException(
                    validation.Issues.Any(issue => issue.Code == ArchiveV1IssueCode.UnsupportedVersion)
                        ? NotebookMigrationErrorCode.UnsupportedSource
                        : NotebookMigrationErrorCode.InvalidSource,
                    "The CM1 archive failed validation.");
            }

            var metadata = new MetadataSink();
            var replay = await reader.ReadPrevalidatedAsync(stream, metadata, cancellationToken)
                .ConfigureAwait(false);
            if (!replay.IsValid)
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The CM1 archive changed after validation.");
            }
            return new ArchiveContentModel1MigrationSource(
                sourcePath,
                stream,
                reader,
                metadata.Rows.AsReadOnly());
        }
        catch
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async IAsyncEnumerable<LegacyPageRecord> ReadPagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var channel = Channel.CreateBounded<LegacyPageRecord>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var producer = ProduceAsync(channel.Writer, producerCancellation.Token);
        try
        {
            await foreach (var page in channel.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return page;
            }
            await producer.ConfigureAwait(false);
        }
        finally
        {
            producerCancellation.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (producerCancellation.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync() =>
        await _stream.DisposeAsync().ConfigureAwait(false);

    private async Task ProduceAsync(
        ChannelWriter<LegacyPageRecord> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await _reader.ReadPrevalidatedAsync(
                    _stream,
                    new PageSink(writer),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!report.IsValid)
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The CM1 archive changed during migration.");
            }
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            throw;
        }
    }

    private sealed class MetadataSink : IArchiveV1RecordSink
    {
        internal List<LegacyMetadataRecord> Rows { get; } = new();

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken)
        {
            Rows.Add(new LegacyMetadataRecord(row.Key, row.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePageAsync(
            ArchiveV1PageRow row,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class PageSink : IArchiveV1RecordSink
    {
        private readonly ChannelWriter<LegacyPageRecord> _writer;

        internal PageSink(ChannelWriter<LegacyPageRecord> writer)
        {
            _writer = writer;
        }

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WritePageAsync(
            ArchiveV1PageRow row,
            CancellationToken cancellationToken) =>
            _writer.WriteAsync(
                new LegacyPageRecord(
                    row.Rowid,
                    row.Uuid,
                    row.Name,
                    row.Index,
                    row.Tags,
                    row.ContentType,
                    row.CreateTime,
                    row.UpdateTime,
                    row.IsErased,
                    row.Text,
                    Array.Empty<string>()),
                cancellationToken);
    }
}
