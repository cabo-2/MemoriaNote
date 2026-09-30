using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MemoriaNote.Archive;

namespace MemoriaNote.Persistence
{
    internal sealed class SqliteArchiveV1PersistenceAdapter
    {
        static readonly string[] RequiredTriggers =
        {
            "Pages_Delete",
            "Pages_Insert",
            "Pages_Update"
        };

        readonly INotebookDbContextFactory _databaseFactory;

        internal SqliteArchiveV1PersistenceAdapter(INotebookDbContextFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        internal async Task<SqliteArchiveV1Snapshot> OpenSnapshotAsync(
            string databasePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = Path.GetFullPath(databasePath);
            if (!File.Exists(normalizedPath))
                throw new FileNotFoundException("The source notebook does not exist.", normalizedPath);

            var connection = new SqliteConnection(CreateConnectionString(
                normalizedPath,
                SqliteOpenMode.ReadOnly));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var transaction = connection.BeginTransaction(deferred: true);
                try
                {
                    await ValidateCurrentSchemaAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var metadata = await ReadMetadataAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var sourceVersion = metadata
                        .SingleOrDefault(row => row.Key == "Version")
                        ?.Value;
                    return new SqliteArchiveV1Snapshot(
                        connection,
                        transaction,
                        metadata,
                        sourceVersion);
                }
                catch
                {
                    transaction.Dispose();
                    throw;
                }
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task CreateCurrentDatabaseAsync(
            string databasePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var context = _databaseFactory.CreateDbContext(databasePath);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async Task<SqliteArchiveV1RestoreWriteResult> RestoreAsync(
            string databasePath,
            Stream archive,
            ArchiveV1Reader reader,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(CreateConnectionString(
                Path.GetFullPath(databasePath),
                SqliteOpenMode.ReadWrite));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA locking_mode=EXCLUSIVE;",
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA journal_mode=MEMORY;",
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA synchronous=OFF;",
                    cancellationToken)
                .ConfigureAwait(false);

            var triggerSql = await ReadTriggerSqlAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            using var transaction = connection.BeginTransaction(deferred: false);
            try
            {
                foreach (var trigger in RequiredTriggers)
                {
                    await ExecuteNonQueryAsync(
                            connection,
                            "DROP TRIGGER \"" + trigger + "\";",
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                using var sink = new RestoreSink(connection, transaction);
                var report = await reader.ReadPrevalidatedAsync(
                        archive,
                        sink,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!report.IsValid)
                {
                    transaction.Rollback();
                    return new SqliteArchiveV1RestoreWriteResult(report, null);
                }

                await RebuildReadModelsAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var sql in triggerSql)
                {
                    await ExecuteNonQueryAsync(
                            connection,
                            sql,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                transaction.Commit();
                await RestoreDurableConnectionSettingsAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                return new SqliteArchiveV1RestoreWriteResult(report, sink.CreateSummary());
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (SqliteException)
                {
                }
                throw;
            }
        }

        internal async Task<bool> VerifyAsync(
            string databasePath,
            SqliteArchiveV1RestoreSummary expected,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SqliteConnection.ClearAllPools();
            await using (var connection = new SqliteConnection(CreateConnectionString(
                Path.GetFullPath(databasePath),
                SqliteOpenMode.ReadOnly)))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction(deferred: true);
                await ValidateCurrentSchemaAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!await IntegrityCheckAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return false;
                }

                var actual = await ReadFingerprintAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!expected.Matches(actual))
                    return false;
                transaction.Commit();
            }

            if (!await VerifyReadModelsAsync(databasePath, cancellationToken)
                .ConfigureAwait(false))
                return false;

            SqliteConnection.ClearAllPools();
            return !File.Exists(databasePath + "-journal") &&
                !File.Exists(databasePath + "-shm") &&
                !File.Exists(databasePath + "-wal");
        }

        internal static void FlushToDisk(string databasePath)
        {
            SqliteConnection.ClearAllPools();
            using var stream = new FileStream(
                databasePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read);
            stream.Flush(flushToDisk: true);
        }

        static async Task<bool> VerifyReadModelsAsync(
            string databasePath,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(CreateConnectionString(
                Path.GetFullPath(databasePath),
                SqliteOpenMode.ReadWrite));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction(deferred: true);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 0;
                command.CommandText = @"
                    SELECT
                        (SELECT COUNT(DISTINCT name) FROM sqlite_schema
                         WHERE type='trigger'
                           AND name IN (
                               'Pages_Delete', 'Pages_Insert', 'Pages_Update')) = 3
                        AND EXISTS(
                            SELECT 1 FROM sqlite_schema
                            WHERE type='table' AND name='FtsIndex')
                        AND NOT EXISTS(
                            SELECT 1 FROM (
                                SELECT
                                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                                    CreateTime, UpdateTime, IsErased
                                FROM Pages
                                EXCEPT
                                SELECT
                                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                                    CreateTime, UpdateTime, IsErased
                                FROM Contents))
                        AND NOT EXISTS(
                            SELECT 1 FROM (
                                SELECT
                                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                                    CreateTime, UpdateTime, IsErased
                                FROM Contents
                                EXCEPT
                                SELECT
                                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                                    CreateTime, UpdateTime, IsErased
                                FROM Pages));";
                var result = await command.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) != 1)
                    return false;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 0;
                command.CommandText =
                    "INSERT INTO FtsIndex(FtsIndex, rank) VALUES('integrity-check', 1);";
                try
                {
                    await command.ExecuteNonQueryAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SqliteException exception) when (
                    exception.SqliteErrorCode == 11 ||
                    exception.SqliteExtendedErrorCode == 267)
                {
                    return false;
                }
            }

            transaction.Commit();
            return true;
        }

        async Task ValidateCurrentSchemaAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            string[] expected;
            using (var context = _databaseFactory.CreateDbContext(connection.DataSource))
                expected = context.Database.GetMigrations().ToArray();

            var actual = new List<string>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 0;
            command.CommandText =
                "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            await using var result = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
                actual.Add(result.GetString(0));

            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidDataException("The notebook schema is not current.");
        }

        static async Task<List<ArchiveV1MetadataRow>> ReadMetadataAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            var rows = new List<ArchiveV1MetadataRow>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 0;
            command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
            await using var result = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new ArchiveV1MetadataRow(
                    result.GetString(0),
                    ReadNullableString(result, 1)));
            }
            return rows;
        }

        static async Task<IReadOnlyList<string>> ReadTriggerSqlAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            var sqlByName = new Dictionary<string, string>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandTimeout = 0;
            command.CommandText =
                "SELECT name, sql FROM sqlite_schema " +
                "WHERE type='trigger' AND name IN " +
                "('Pages_Delete', 'Pages_Insert', 'Pages_Update');";
            await using var result = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
                sqlByName.Add(result.GetString(0), result.GetString(1));

            if (!RequiredTriggers.All(sqlByName.ContainsKey))
                throw new InvalidDataException("The current notebook triggers are incomplete.");
            return RequiredTriggers.Select(name => sqlByName[name]).ToArray();
        }

        static async Task RebuildReadModelsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                DELETE FROM Contents;
                INSERT INTO Contents(
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased)
                SELECT
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased
                FROM Pages;
                INSERT INTO FtsIndex(FtsIndex) VALUES('rebuild');";
            await ExecuteNonQueryAsync(
                    connection,
                    sql,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        static async Task RestoreDurableConnectionSettingsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA locking_mode=NORMAL;",
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "SELECT count(*) FROM sqlite_schema;",
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA journal_mode=DELETE;",
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA synchronous=FULL;",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        static async Task<bool> IntegrityCheckAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 0;
            command.CommandText = "PRAGMA integrity_check;";
            await using var result = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            var found = false;
            while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                found = true;
                if (!string.Equals(result.GetString(0), "ok", StringComparison.Ordinal))
                    return false;
            }
            return found;
        }

        static async Task<ArchiveV1DatabaseFingerprint> ReadFingerprintAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            var fingerprint = new ArchiveV1DatabaseFingerprint();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 0;
                command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
                await using var result = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    fingerprint.AddMetadata(new ArchiveV1MetadataRow(
                        result.GetString(0),
                        ReadNullableString(result, 1)));
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 0;
                command.CommandText = @"
                    SELECT
                        Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                        CreateTime, UpdateTime, IsErased, Text
                    FROM Pages
                    ORDER BY Rowid;";
                await using var result = await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    fingerprint.AddPage(new ArchiveV1PageRow(
                        result.GetInt32(0),
                        result.GetString(1),
                        ReadNullableString(result, 2),
                        result.GetInt32(3),
                        ReadNullableString(result, 4),
                        ReadNullableString(result, 5),
                        result.GetString(6),
                        result.GetString(7),
                        result.GetInt32(8),
                        ReadNullableString(result, 9)));
                }
            }
            return fingerprint;
        }

        static Task<int> ExecuteNonQueryAsync(
            SqliteConnection connection,
            string sql,
            CancellationToken cancellationToken)
        {
            return ExecuteNonQueryAsync(connection, sql, null, cancellationToken);
        }

        static async Task<int> ExecuteNonQueryAsync(
            SqliteConnection connection,
            string sql,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 0;
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        static string CreateConnectionString(string databasePath, SqliteOpenMode mode)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = mode,
                Pooling = false
            }.ToString();
        }

        static string ReadNullableString(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        sealed class RestoreSink : IArchiveV1RecordSink, IDisposable
        {
            readonly SqliteCommand _metadataCommand;
            readonly SqliteCommand _pageCommand;
            readonly ArchiveV1DatabaseFingerprint _fingerprint =
                new ArchiveV1DatabaseFingerprint();

            internal RestoreSink(SqliteConnection connection, SqliteTransaction transaction)
            {
                _metadataCommand = connection.CreateCommand();
                _metadataCommand.Transaction = transaction;
                _metadataCommand.CommandTimeout = 0;
                _metadataCommand.CommandText =
                    "INSERT INTO Metadata(Key, Value) VALUES($key, $value);";
                _metadataCommand.Parameters.Add("$key", SqliteType.Text);
                _metadataCommand.Parameters.Add("$value", SqliteType.Text);
                _metadataCommand.Prepare();

                _pageCommand = connection.CreateCommand();
                _pageCommand.Transaction = transaction;
                _pageCommand.CommandTimeout = 0;
                _pageCommand.CommandText = @"
                    INSERT INTO Pages(
                        Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                        CreateTime, UpdateTime, IsErased, Text)
                    VALUES(
                        $rowid, $uuid, $name, $index, $tags, $contentType,
                        $createTime, $updateTime, $isErased, $text);";
                _pageCommand.Parameters.Add("$rowid", SqliteType.Integer);
                _pageCommand.Parameters.Add("$uuid", SqliteType.Text);
                _pageCommand.Parameters.Add("$name", SqliteType.Text);
                _pageCommand.Parameters.Add("$index", SqliteType.Integer);
                _pageCommand.Parameters.Add("$tags", SqliteType.Text);
                _pageCommand.Parameters.Add("$contentType", SqliteType.Text);
                _pageCommand.Parameters.Add("$createTime", SqliteType.Text);
                _pageCommand.Parameters.Add("$updateTime", SqliteType.Text);
                _pageCommand.Parameters.Add("$isErased", SqliteType.Integer);
                _pageCommand.Parameters.Add("$text", SqliteType.Text);
                _pageCommand.Prepare();
            }

            public ValueTask WriteMetadataAsync(
                ArchiveV1MetadataRow row,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _metadataCommand.Parameters["$key"].Value = row.Key;
                _metadataCommand.Parameters["$value"].Value = DbValue(row.Value);
                _metadataCommand.ExecuteNonQuery();
                _fingerprint.AddMetadata(row);
                return ValueTask.CompletedTask;
            }

            public ValueTask WritePageAsync(
                ArchiveV1PageRow row,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _pageCommand.Parameters["$rowid"].Value = row.Rowid;
                _pageCommand.Parameters["$uuid"].Value = row.Uuid;
                _pageCommand.Parameters["$name"].Value = DbValue(row.Name);
                _pageCommand.Parameters["$index"].Value = row.Index;
                _pageCommand.Parameters["$tags"].Value = DbValue(row.Tags);
                _pageCommand.Parameters["$contentType"].Value = DbValue(row.ContentType);
                _pageCommand.Parameters["$createTime"].Value = row.CreateTime;
                _pageCommand.Parameters["$updateTime"].Value = row.UpdateTime;
                _pageCommand.Parameters["$isErased"].Value = row.IsErased;
                _pageCommand.Parameters["$text"].Value = DbValue(row.Text);
                _pageCommand.ExecuteNonQuery();
                _fingerprint.AddPage(row);
                return ValueTask.CompletedTask;
            }

            internal SqliteArchiveV1RestoreSummary CreateSummary()
            {
                return new SqliteArchiveV1RestoreSummary(_fingerprint);
            }

            public void Dispose()
            {
                _metadataCommand.Dispose();
                _pageCommand.Dispose();
            }

            static object DbValue(string value)
            {
                return value == null ? DBNull.Value : value;
            }
        }
    }

    internal sealed class SqliteArchiveV1Snapshot : IAsyncDisposable
    {
        readonly SqliteConnection _connection;
        readonly SqliteTransaction _transaction;
        bool _pagesRead;

        internal SqliteArchiveV1Snapshot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IReadOnlyList<ArchiveV1MetadataRow> metadata,
            string sourceNotebookFormatVersion)
        {
            _connection = connection;
            _transaction = transaction;
            Metadata = metadata;
            SourceNotebookFormatVersion = sourceNotebookFormatVersion;
        }

        internal IReadOnlyList<ArchiveV1MetadataRow> Metadata { get; }

        internal string SourceNotebookFormatVersion { get; }

        internal int PageCount { get; private set; }

        internal async IAsyncEnumerable<ArchiveV1PageRow> ReadPagesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_pagesRead)
                throw new InvalidOperationException("The snapshot pages can only be read once.");
            _pagesRead = true;

            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandTimeout = 0;
            command.CommandText = @"
                SELECT
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased, Text
                FROM Pages
                ORDER BY Rowid;";
            await using var result = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await result.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PageCount++;
                yield return new ArchiveV1PageRow(
                    result.GetInt32(0),
                    result.GetString(1),
                    ReadNullableString(result, 2),
                    result.GetInt32(3),
                    ReadNullableString(result, 4),
                    ReadNullableString(result, 5),
                    result.GetString(6),
                    result.GetString(7),
                    result.GetInt32(8),
                    ReadNullableString(result, 9));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        static string ReadNullableString(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }

    internal sealed class SqliteArchiveV1RestoreWriteResult
    {
        internal SqliteArchiveV1RestoreWriteResult(
            ArchiveV1ValidationReport validationReport,
            SqliteArchiveV1RestoreSummary summary)
        {
            ValidationReport = validationReport;
            Summary = summary;
        }

        internal ArchiveV1ValidationReport ValidationReport { get; }

        internal SqliteArchiveV1RestoreSummary Summary { get; }
    }

    internal sealed class SqliteArchiveV1RestoreSummary
    {
        readonly ArchiveV1DatabaseFingerprint _fingerprint;

        internal SqliteArchiveV1RestoreSummary(ArchiveV1DatabaseFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
        }

        internal int MetadataCount => _fingerprint.Metadata.Count;

        internal int PageCount => _fingerprint.Pages.Count;

        internal bool Matches(ArchiveV1DatabaseFingerprint actual)
        {
            return _fingerprint.Equals(actual);
        }
    }

    internal sealed class ArchiveV1DatabaseFingerprint : IEquatable<ArchiveV1DatabaseFingerprint>
    {
        internal ArchiveV1FingerprintAccumulator Metadata { get; } =
            new ArchiveV1FingerprintAccumulator();

        internal ArchiveV1FingerprintAccumulator Pages { get; } =
            new ArchiveV1FingerprintAccumulator();

        internal void AddMetadata(ArchiveV1MetadataRow row)
        {
            Metadata.Add(hash =>
            {
                AppendString(hash, row.Key);
                AppendString(hash, row.Value);
            });
        }

        internal void AddPage(ArchiveV1PageRow row)
        {
            Pages.Add(hash =>
            {
                AppendInt32(hash, row.Rowid);
                AppendString(hash, row.Uuid);
                AppendString(hash, row.Name);
                AppendInt32(hash, row.Index);
                AppendString(hash, row.Tags);
                AppendString(hash, row.ContentType);
                AppendString(hash, row.CreateTime);
                AppendString(hash, row.UpdateTime);
                AppendInt32(hash, row.IsErased);
                AppendString(hash, row.Text);
            });
        }

        public bool Equals(ArchiveV1DatabaseFingerprint other)
        {
            return other != null &&
                Metadata.Equals(other.Metadata) &&
                Pages.Equals(other.Pages);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ArchiveV1DatabaseFingerprint);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Metadata, Pages);
        }

        static void AppendInt32(IncrementalHash hash, int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.AppendData(bytes);
        }

        static void AppendString(IncrementalHash hash, string value)
        {
            if (value == null)
            {
                hash.AppendData(new byte[] { 0 });
                return;
            }

            hash.AppendData(new byte[] { 1 });
            AppendInt32(hash, value.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                var encoder = Encoding.UTF8.GetEncoder();
                var offset = 0;
                do
                {
                    encoder.Convert(
                        value.AsSpan(offset),
                        buffer.AsSpan(),
                        flush: true,
                        out var charsUsed,
                        out var bytesUsed,
                        out var completed);
                    if (bytesUsed > 0)
                        hash.AppendData(buffer.AsSpan(0, bytesUsed));
                    offset += charsUsed;
                    if (completed)
                        break;
                }
                while (offset < value.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal sealed class ArchiveV1FingerprintAccumulator :
        IEquatable<ArchiveV1FingerprintAccumulator>
    {
        readonly byte[] _sum = new byte[32];
        readonly byte[] _xor = new byte[32];

        internal int Count { get; private set; }

        internal void Add(Action<IncrementalHash> appendRecord)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            appendRecord(hash);
            var recordHash = hash.GetHashAndReset();
            var carry = 0;
            for (var index = 0; index < recordHash.Length; index++)
            {
                _xor[index] ^= recordHash[index];
                var sum = _sum[index] + recordHash[index] + carry;
                _sum[index] = (byte)sum;
                carry = sum >> 8;
            }
            Count++;
        }

        public bool Equals(ArchiveV1FingerprintAccumulator other)
        {
            return other != null &&
                Count == other.Count &&
                _sum.SequenceEqual(other._sum) &&
                _xor.SequenceEqual(other._xor);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ArchiveV1FingerprintAccumulator);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Count, _sum[0], _xor[0]);
        }
    }
}
