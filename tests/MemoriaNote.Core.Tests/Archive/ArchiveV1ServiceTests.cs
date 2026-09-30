using MemoriaNote.Archive;
using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Verifies archive v1 backup and restore service behavior.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ArchiveV1ServiceTests
{
    /// <summary>
    /// Verifies a file round trip preserves all raw authoritative values and rebuilds read models.
    /// </summary>
    [Test]
    public async Task FileRoundTrip_PreservesRawRowsAndRebuildsReadModels()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("archive-v1", "Archive V1");
        SeedRawRows(database.DatabasePath);
        var archivePath = Path.Combine(database.DirectoryPath, "backup.mnarchive");
        var restoredPath = Path.Combine(database.DirectoryPath, "restored.mnote");
        using var services = CreateServices(database.DirectoryPath);

        var backup = await services.Backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
            CancellationToken.None);
        var restore = await services.Restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archivePath, restoredPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.IsSuccess, Is.True);
            Assert.That(backup.MetadataCount, Is.EqualTo(ReadMetadata(database.DatabasePath).Count));
            Assert.That(backup.PageCount, Is.EqualTo(2));
            Assert.That(restore.IsSuccess, Is.True);
            Assert.That(restore.MetadataCount, Is.EqualTo(backup.MetadataCount));
            Assert.That(restore.PageCount, Is.EqualTo(backup.PageCount));
            Assert.That(ReadMetadata(restoredPath), Is.EqualTo(ReadMetadata(database.DatabasePath)));
            Assert.That(ReadPages(restoredPath), Is.EqualTo(ReadPages(database.DatabasePath)));
        }

        var factory = CreateDatabaseFactory();
        var readModels = await new SqliteNotebookReadModelMaintenance(factory)
            .CheckIntegrityAsync(restoredPath, CancellationToken.None);
        Assert.That(readModels.IsConsistent, Is.True);
    }

    /// <summary>
    /// Verifies stream backup and non-seekable stream restore share the same service boundary.
    /// </summary>
    [Test]
    public async Task StreamRoundTrip_NonSeekableInput_LeavesCallerStreamsOpen()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("stream", "Stream archive");
        SeedRawRows(database.DatabasePath);
        var restoredPath = Path.Combine(database.DirectoryPath, "stream-restored.mnote");
        using var services = CreateServices(database.DirectoryPath);
        using var output = new TrackingMemoryStream();

        var backup = await services.Backup.BackupAsync(
            new ArchiveV1StreamBackupRequest(database.DatabasePath, output),
            CancellationToken.None);
        var archiveBytes = output.ToArray();
        using var input = new NonSeekableReadStream(archiveBytes);
        var restore = await services.Restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, restoredPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.IsSuccess, Is.True);
            Assert.That(restore.IsSuccess, Is.True);
            Assert.That(output.WasDisposed, Is.False);
            Assert.That(input.WasDisposed, Is.False);
            Assert.That(ReadMetadata(restoredPath), Is.EqualTo(ReadMetadata(database.DatabasePath)));
            Assert.That(ReadPages(restoredPath), Is.EqualTo(ReadPages(database.DatabasePath)));
        }
    }

    /// <summary>Verifies metadata and pages are read from one SQLite snapshot.</summary>
    [Test]
    public async Task Backup_ConcurrentSourceChange_UsesOneDatabaseSnapshot()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("snapshot", "Before snapshot");
        SeedRawRows(database.DatabasePath);
        EnableWal(database.DatabasePath);
        var expectedMetadata = ReadMetadata(database.DatabasePath);
        var expectedPages = ReadPages(database.DatabasePath);
        var archivePath = Path.Combine(database.DirectoryPath, "snapshot.mnarchive");
        var restoredPath = Path.Combine(database.DirectoryPath, "snapshot.mnote");
        using var clock = new BlockingClock(
            new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero));
        using var services = CreateServices(database.DirectoryPath, clock);

        var backupTask = Task.Run(async () => await services.Backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
            CancellationToken.None));
        try
        {
            Assert.That(clock.ReadStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            ChangeSourceAfterSnapshot(database.DatabasePath);
        }
        finally
        {
            clock.Continue();
        }

        var backup = await backupTask;
        var restore = await services.Restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archivePath, restoredPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.IsSuccess, Is.True);
            Assert.That(restore.IsSuccess, Is.True);
            Assert.That(ReadMetadata(restoredPath), Is.EqualTo(expectedMetadata));
            Assert.That(ReadPages(restoredPath), Is.EqualTo(expectedPages));
            Assert.That(ReadMetadata(database.DatabasePath), Is.Not.EqualTo(expectedMetadata));
            Assert.That(ReadPages(database.DatabasePath), Is.Not.EqualTo(expectedPages));
        }
    }

    /// <summary>Verifies existing backup and restore destinations are never replaced.</summary>
    [Test]
    public async Task ExistingDestinations_ArePreserved()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("conflict", "Conflict");
        var archivePath = Path.Combine(database.DirectoryPath, "conflict.mnarchive");
        var restoredPath = Path.Combine(database.DirectoryPath, "conflict.mnote");
        await File.WriteAllTextAsync(archivePath, "keep archive");
        await File.WriteAllTextAsync(restoredPath, "keep notebook");
        using var services = CreateServices(database.DirectoryPath);

        var backup = await services.Backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
            CancellationToken.None);
        using var validArchive = new MemoryStream();
        var streamBackup = await services.Backup.BackupAsync(
            new ArchiveV1StreamBackupRequest(database.DatabasePath, validArchive),
            CancellationToken.None);
        var restore = await services.Restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(
                new MemoryStream(validArchive.ToArray()),
                restoredPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.DestinationConflict));
            Assert.That(streamBackup.IsSuccess, Is.True);
            Assert.That(restore.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.DestinationConflict));
            Assert.That(await File.ReadAllTextAsync(archivePath), Is.EqualTo("keep archive"));
            Assert.That(await File.ReadAllTextAsync(restoredPath), Is.EqualTo("keep notebook"));
        }
    }

    /// <summary>Verifies restore rejects a conflict without consuming its input stream.</summary>
    [Test]
    public async Task StreamRestore_WhenDestinationExists_DoesNotReadInput()
    {
        using var database = new TemporaryNotebookDatabase();
        var destinationPath = Path.Combine(database.DirectoryPath, "existing.mnote");
        await File.WriteAllTextAsync(destinationPath, "keep");
        using var services = CreateServices(database.DirectoryPath);
        using var input = new ThrowingReadStream();

        var result = await services.Restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.DestinationConflict));
            Assert.That(input.ReadAttempted, Is.False);
            Assert.That(input.WasDisposed, Is.False);
            Assert.That(await File.ReadAllTextAsync(destinationPath), Is.EqualTo("keep"));
        }
    }

    /// <summary>Verifies invalid archives are rejected before a database is created.</summary>
    [Test]
    public async Task InvalidArchive_IsRejectedBeforeDatabaseCreation()
    {
        using var database = new TemporaryNotebookDatabase();
        var destinationPath = Path.Combine(database.DirectoryPath, "invalid.mnote");
        using var services = CreateServices(database.DirectoryPath);
        using var input = new NonSeekableReadStream("not a zip"u8.ToArray());

        var result = await services.Restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.ArchiveValidationFailed));
            Assert.That(
                result.Error?.ValidationReport?.Classification,
                Is.EqualTo(ArchiveV1Classification.MalformedZip));
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListTemporaryOutputs(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies pre-canceled operations create no output and preserve caller streams.</summary>
    [Test]
    public void PreCanceledOperations_CreateNoOutputs()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("cancel", "Cancel");
        var archivePath = Path.Combine(database.DirectoryPath, "cancel.mnarchive");
        var restoredPath = Path.Combine(database.DirectoryPath, "cancel.mnote");
        using var services = CreateServices(database.DirectoryPath);
        using var output = new TrackingMemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await services.Backup.BackupAsync(
                new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
                cancellation.Token));
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await services.Backup.BackupAsync(
                new ArchiveV1StreamBackupRequest(database.DatabasePath, output),
                cancellation.Token));
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await services.Restore.RestoreAsync(
                new ArchiveV1StreamRestoreRequest(
                    new NonSeekableReadStream("cancel"u8.ToArray()),
                    restoredPath),
                cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(archivePath), Is.False);
            Assert.That(File.Exists(restoredPath), Is.False);
            Assert.That(output.Length, Is.Zero);
            Assert.That(ListTemporaryOutputs(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies cancellation after snapshot creation removes the owned archive.</summary>
    [Test]
    public async Task Backup_CanceledAfterSnapshot_CleansTemporaryOutput()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("cancel-midway", "Cancel midway");
        var archivePath = Path.Combine(database.DirectoryPath, "cancel-midway.mnarchive");
        using var clock = new BlockingClock(
            new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero));
        using var services = CreateServices(database.DirectoryPath, clock);
        using var cancellation = new CancellationTokenSource();

        var backupTask = Task.Run(async () => await services.Backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
            cancellation.Token));
        try
        {
            Assert.That(clock.ReadStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            cancellation.Cancel();
        }
        finally
        {
            clock.Continue();
        }

        Assert.ThrowsAsync<OperationCanceledException>(async () => await backupTask);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(archivePath), Is.False);
            Assert.That(ListTemporaryOutputs(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies an invalid source notebook cannot publish bytes or a file.</summary>
    [Test]
    public async Task InvalidSourceNotebook_PublishesNothing()
    {
        using var database = new TemporaryNotebookDatabase();
        var invalidDatabasePath = Path.Combine(database.DirectoryPath, "invalid.db");
        await File.WriteAllTextAsync(invalidDatabasePath, "not sqlite");
        var archivePath = Path.Combine(database.DirectoryPath, "invalid.mnarchive");
        using var services = CreateServices(database.DirectoryPath);
        using var output = new TrackingMemoryStream();

        var fileResult = await services.Backup.BackupAsync(
            new ArchiveV1FileBackupRequest(invalidDatabasePath, archivePath),
            CancellationToken.None);
        var streamResult = await services.Backup.BackupAsync(
            new ArchiveV1StreamBackupRequest(invalidDatabasePath, output),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fileResult.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.SourceNotebookInvalid));
            Assert.That(streamResult.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.SourceNotebookInvalid));
            Assert.That(File.Exists(archivePath), Is.False);
            Assert.That(output.Length, Is.Zero);
            Assert.That(ListTemporaryOutputs(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies caller stream failures are classified and owned temporaries are removed.</summary>
    [Test]
    public async Task StreamIoFailures_AreReportedAndCleanedWithoutClosingStreams()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("io", "I/O failures");
        using var services = CreateServices(database.DirectoryPath);
        using var output = new ThrowingWriteStream();
        using var input = new ThrowingReadStream();
        var destinationPath = Path.Combine(database.DirectoryPath, "io.mnote");

        var backup = await services.Backup.BackupAsync(
            new ArchiveV1StreamBackupRequest(database.DatabasePath, output),
            CancellationToken.None);
        var restore = await services.Restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.IoFailure));
            Assert.That(restore.Error?.Code, Is.EqualTo(
                ArchiveV1OperationErrorCode.IoFailure));
            Assert.That(output.WasDisposed, Is.False);
            Assert.That(input.WasDisposed, Is.False);
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListTemporaryOutputs(database.DirectoryPath), Is.Empty);
        }
    }

    static ServiceScope CreateServices(string rootDirectory, IClock? clock = null)
    {
        var temporaryDirectory = Path.Combine(rootDirectory, "service-temp");
        var temporaryFileStore = new TemporaryFileStore(temporaryDirectory);
        var factory = CreateDatabaseFactory();
        var backup = new ArchiveV1BackupService(
            factory,
            temporaryFileStore,
            clock ?? new FixedClock(
                new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero)),
            new ArchiveV1Creator("MemoriaNote.Tests", "1.0.0"));
        var restore = new ArchiveV1RestoreService(factory, temporaryFileStore);
        return new ServiceScope(backup, restore, temporaryFileStore);
    }

    static SqliteNotebookDbContextFactory CreateDatabaseFactory()
    {
        return new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
    }

    static void SeedRawRows(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = @"
                INSERT INTO Metadata(Key, Value) VALUES('Unknown-雪', NULL);
                INSERT INTO Metadata(Key, Value) VALUES('Empty', '');";
            metadata.ExecuteNonQuery();
        }

        using (var pages = connection.CreateCommand())
        {
            pages.Transaction = transaction;
            pages.CommandText = @"
                INSERT INTO Pages(
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased, Text)
                VALUES(
                    7, 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', NULL, 1,
                    ' { ""dir"" : ""日本/雪"" } ', NULL,
                    '2026-01-02 03:04:05.1234000',
                    '2026-02-03 04:05:06', 0, NULL);
                INSERT INTO Pages(
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased, Text)
                VALUES(
                    42, '11111111-2222-3333-4444-555555555555', '同名', 2,
                    '', 'T', '2026-03-04 05:06:07',
                    '2026-03-04 05:06:07.1', 1, 'line 1
line 2\n終');";
            pages.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    static void EnableWal(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        Assert.That(command.ExecuteScalar(), Is.EqualTo("wal"));
    }

    static void ChangeSourceAfterSnapshot(string databasePath)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            UPDATE Metadata SET Value='After snapshot' WHERE Key='Title';
            INSERT INTO Pages(
                Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text)
            VALUES(
                100, '99999999-8888-7777-6666-555555555555',
                'After', 1, NULL, 'T',
                '2026-09-30 01:02:03', '2026-09-30 01:02:03', 0,
                'created after snapshot');";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    static IReadOnlyList<RawMetadataRow> ReadMetadata(string databasePath)
    {
        var rows = new List<RawMetadataRow>();
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawMetadataRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return rows;
    }

    static IReadOnlyList<RawPageRow> ReadPages(string databasePath)
    {
        var rows = new List<RawPageRow>();
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text
            FROM Pages
            ORDER BY Rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawPageRow(
                reader.GetInt32(0),
                reader.GetString(1),
                NullableString(reader, 2),
                reader.GetInt32(3),
                NullableString(reader, 4),
                NullableString(reader, 5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                NullableString(reader, 9)));
        }
        return rows;
    }

    static string? NullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    static IReadOnlyList<string> ListTemporaryOutputs(string rootDirectory)
    {
        return Directory
            .EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(".tmp", StringComparison.Ordinal) ||
                path.Contains("service-temp", StringComparison.Ordinal))
            .ToArray();
    }

    sealed class ServiceScope : IDisposable
    {
        readonly TemporaryFileStore _temporaryFileStore;

        internal ServiceScope(
            ArchiveV1BackupService backup,
            ArchiveV1RestoreService restore,
            TemporaryFileStore temporaryFileStore)
        {
            Backup = backup;
            Restore = restore;
            _temporaryFileStore = temporaryFileStore;
        }

        internal ArchiveV1BackupService Backup { get; }

        internal ArchiveV1RestoreService Restore { get; }

        public void Dispose()
        {
            _temporaryFileStore.Dispose();
        }
    }

    sealed class TrackingMemoryStream : MemoryStream
    {
        internal bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    sealed class BlockingClock : IClock, IDisposable
    {
        readonly ManualResetEventSlim _continue = new(initialState: false);
        readonly DateTimeOffset _utcNow;

        internal BlockingClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        internal ManualResetEventSlim ReadStarted { get; } = new(initialState: false);

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadStarted.Set();
                _continue.Wait();
                return _utcNow;
            }
        }

        internal void Continue()
        {
            _continue.Set();
        }

        public void Dispose()
        {
            _continue.Set();
            _continue.Dispose();
            ReadStarted.Dispose();
        }
    }

    sealed class NonSeekableReadStream : Stream
    {
        readonly MemoryStream _inner;

        internal NonSeekableReadStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes, writable: false);
        }

        internal bool WasDisposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class ThrowingReadStream : Stream
    {
        internal bool ReadAttempted { get; private set; }

        internal bool WasDisposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttempted = true;
            throw new IOException("Injected read failure.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadAttempted = true;
            return ValueTask.FromException<int>(new IOException("Injected read failure."));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    sealed class ThrowingWriteStream : Stream
    {
        internal bool WasDisposed { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Injected write failure.");

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new IOException("Injected write failure."));
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    sealed record RawMetadataRow(string Key, string? Value);

    sealed record RawPageRow(
        int Rowid,
        string Uuid,
        string? Name,
        int Index,
        string? Tags,
        string? ContentType,
        string CreateTime,
        string UpdateTime,
        int IsErased,
        string? Text);
}
