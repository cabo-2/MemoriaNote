using System.IO.Compression;
using MemoriaNote.Archive;
using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Verifies Schema V2 backup and native CM2 restore workflows.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ArchiveV1ContentModel2ServiceTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "reference",
        "archive",
        "v1-content-model-v2",
        "examples",
        "compatibility",
        "full-field");

    /// <summary>Verifies fixture restore and backup/restore preserve every authoritative value.</summary>
    [Test]
    public async Task FullFieldFixture_RoundTripsSchemaV2AuthoritativeValues()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var restore = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        var backup = new ArchiveV1ContentModel2BackupService(
            temporaryFiles,
            new FixedClock(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)),
            new ArchiveV1Creator("MemoriaNote.Tests", "ecli-1130b"));
        var firstPath = directory.GetPath("first.mnote");
        var archivePath = directory.GetPath("backup.mnarchive");
        var secondPath = directory.GetPath("second.mnote");
        await using var fixture = CreateFixtureArchive();

        var firstRestore = await restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(fixture, firstPath),
            CancellationToken.None);
        var firstValues = await ReadValuesAsync(firstPath);
        var sourceBytes = await File.ReadAllBytesAsync(firstPath);
        var backupResult = await backup.BackupAsync(
            new ArchiveV1FileBackupRequest(firstPath, archivePath),
            CancellationToken.None);
        var secondRestore = await restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archivePath, secondPath),
            CancellationToken.None);
        var secondValues = await ReadValuesAsync(secondPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRestore.IsSuccess, Is.True, Describe(firstRestore));
            Assert.That(backupResult.IsSuccess, Is.True, Describe(backupResult));
            Assert.That(secondRestore.IsSuccess, Is.True, Describe(secondRestore));
            Assert.That(firstRestore.MetadataCount, Is.EqualTo(8));
            Assert.That(firstRestore.PageCount, Is.EqualTo(3));
            Assert.That(firstValues.Metadata, Is.EqualTo(secondValues.Metadata));
            Assert.That(firstValues.Pages, Is.EqualTo(secondValues.Pages));
            Assert.That(await File.ReadAllBytesAsync(firstPath), Is.EqualTo(sourceBytes));
            Assert.That(firstValues.InternalIds, Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(firstValues.InternalIds, Does.Not.Contain(9));
            Assert.That(firstValues.InternalIds, Does.Not.Contain(long.MaxValue));
        }

        Assert.That((await SchemaV2DatabaseValidator.ValidateAsync(secondPath)).IsValid, Is.True);
    }

    /// <summary>Verifies non-seekable input is spooled and caller-owned streams remain open.</summary>
    [Test]
    public async Task StreamRestore_NonSeekableInput_LeavesCallerStreamOpen()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var service = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        await using var fixture = CreateFixtureArchive();
        using var input = new TrackingNonSeekableStream(fixture.ToArray());
        var destination = directory.GetPath("stream.mnote");

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(input.WasDisposed, Is.False);
            Assert.That(File.Exists(destination), Is.True);
            Assert.That(Directory.Exists(temporaryFiles.WorkingDirectory), Is.True);
            Assert.That(Directory.GetFiles(temporaryFiles.WorkingDirectory), Is.Empty);
        }
    }

    /// <summary>Verifies dry-run validates records without creating a SQLite output.</summary>
    [Test]
    public async Task DryRun_ValidatesWithoutPublishingDatabase()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var service = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        await using var fixture = CreateFixtureArchive();
        var destination = directory.GetPath("dry-run.mnote");

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(fixture, destination, dryRun: true),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(result.MetadataCount, Is.EqualTo(8));
            Assert.That(result.PageCount, Is.EqualTo(3));
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(Directory.GetFileSystemEntries(directory.Path)
                .Where(path => path != temporaryFiles.WorkingDirectory), Is.Empty);
        }
    }

    /// <summary>Verifies existing output remains unchanged and input is not consumed.</summary>
    [Test]
    public async Task ExistingDestination_IsPreservedBeforeInputIsRead()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var service = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        var destination = directory.GetPath("existing.mnote");
        await File.WriteAllTextAsync(destination, "keep");
        using var input = new ThrowingReadStream();

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.DestinationConflict));
            Assert.That(input.ReadAttempted, Is.False);
            Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("keep"));
        }
    }

    /// <summary>Verifies a SQLite sidecar reserves the complete destination file set.</summary>
    [Test]
    public async Task ExistingDestinationSidecar_IsPreservedBeforeInputIsRead()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var service = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        var destination = directory.GetPath("sidecar.mnote");
        var sidecar = destination + "-wal";
        await File.WriteAllTextAsync(sidecar, "keep");
        using var input = new ThrowingReadStream();

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.DestinationConflict));
            Assert.That(input.ReadAttempted, Is.False);
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(await File.ReadAllTextAsync(sidecar), Is.EqualTo("keep"));
        }
    }

    /// <summary>Verifies malformed input leaves neither a database nor private temporary files.</summary>
    [Test]
    public async Task InvalidArchive_CleansSpoolAndOutput()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var service = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        var destination = directory.GetPath("invalid.mnote");
        await using var input = new MemoryStream("not a zip archive"u8.ToArray());

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destination),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.ArchiveValidationFailed));
            Assert.That(SqliteDatabaseFiles.AnyExists(destination), Is.False);
            Assert.That(Directory.GetFiles(temporaryFiles.WorkingDirectory), Is.Empty);
            Assert.That(Directory.GetDirectories(directory.Path), Is.EqualTo(new[] { temporaryFiles.WorkingDirectory }));
        }
    }

    /// <summary>Verifies backup ignores damaged derived data and restore rebuilds it from authoritative values.</summary>
    [Test]
    public async Task Backup_DerivedMismatch_RestoresValidatedDerivedDataWithoutRepairingSource()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var restore = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        var backup = CreateBackupService(temporaryFiles, new FixedClock(
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));
        var source = directory.GetPath("source.mnote");
        var archive = directory.GetPath("backup.mnarchive");
        var destination = directory.GetPath("restored.mnote");
        await using var fixture = CreateFixtureArchive();
        Assert.That((await restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(fixture, source),
            CancellationToken.None)).IsSuccess, Is.True);
        await ExecuteAsync(source, "DELETE FROM PageTags;");
        var before = await SchemaV2DatabaseValidator.ValidateAsync(source);

        var backupResult = await backup.BackupAsync(
            new ArchiveV1FileBackupRequest(source, archive),
            CancellationToken.None);
        var restoreResult = await restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archive, destination),
            CancellationToken.None);
        var restored = await SchemaV2DatabaseValidator.ValidateAsync(destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.AuthoritativeValues.IsValid, Is.True);
            Assert.That(before.DerivedData.IsValid, Is.False);
            Assert.That(backupResult.IsSuccess, Is.True, Describe(backupResult));
            Assert.That(restoreResult.IsSuccess, Is.True, Describe(restoreResult));
            Assert.That(restored.IsValid, Is.True);
            Assert.That(await ReadScalarAsync<long>(source, "SELECT COUNT(*) FROM PageTags;"), Is.Zero);
            Assert.That(await ReadScalarAsync<long>(destination, "SELECT COUNT(*) FROM PageTags;"), Is.GreaterThan(0));
        }
    }

    /// <summary>Verifies backup exports one SQLite snapshot while a concurrent writer commits.</summary>
    [Test]
    public async Task Backup_ConcurrentWriter_ExportsOneConsistentSnapshot()
    {
        using var directory = new TemporaryDirectory();
        using var temporaryFiles = new TemporaryFileStore(directory.GetPath("temp"));
        var restore = new ArchiveV1ContentModel2RestoreService(temporaryFiles);
        using var clock = new BlockingClock(
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        var backup = CreateBackupService(temporaryFiles, clock);
        var source = directory.GetPath("source.mnote");
        var archive = directory.GetPath("snapshot.mnarchive");
        var destination = directory.GetPath("restored.mnote");
        await using var fixture = CreateFixtureArchive();
        Assert.That((await restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(fixture, source),
            CancellationToken.None)).IsSuccess, Is.True);
        await ExecuteAsync(source, "PRAGMA journal_mode = WAL;");
        var expected = await ReadValuesAsync(source);

        var backupTask = Task.Run(() => backup.BackupAsync(
            new ArchiveV1FileBackupRequest(source, archive),
            CancellationToken.None));
        Task updateTask = Task.CompletedTask;
        try
        {
            Assert.That(clock.WaitUntilRead(TimeSpan.FromSeconds(10)), Is.True);
            updateTask = Task.Run(() => ExecuteAsync(
                    source,
                    """
                    UPDATE Metadata SET Value = 'changed during backup' WHERE Key = 'Title';
                    UPDATE Pages SET Text = 'changed during backup' WHERE InternalId = 1;
                    """));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            clock.Continue();
        }
        var backupResult = await backupTask;
        await updateTask;
        var restoreResult = await restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archive, destination),
            CancellationToken.None);
        var actual = await ReadValuesAsync(destination);
        var changedSource = await ReadValuesAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupResult.IsSuccess, Is.True, Describe(backupResult));
            Assert.That(restoreResult.IsSuccess, Is.True, Describe(restoreResult));
            Assert.That(actual.Metadata, Is.EqualTo(expected.Metadata));
            Assert.That(actual.Pages, Is.EqualTo(expected.Pages));
            Assert.That(changedSource.Metadata, Is.Not.EqualTo(expected.Metadata));
            Assert.That(changedSource.Pages, Is.Not.EqualTo(expected.Pages));
        }
    }

    private static MemoryStream CreateFixtureArchive()
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "manifest.json", "metadata.json", "pages.ndjson" })
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var destination = entry.Open();
                using var input = File.OpenRead(Path.Combine(FixtureDirectory, name));
                input.CopyTo(destination);
            }
        }
        output.Position = 0;
        return output;
    }

    private static ArchiveV1ContentModel2BackupService CreateBackupService(
        ITemporaryFileStore temporaryFiles,
        IClock clock) =>
        new(
            temporaryFiles,
            clock,
            new ArchiveV1Creator("MemoriaNote.Tests", "ecli-1130b"));

    private static async Task ExecuteAsync(string path, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(path, SqliteOpenMode.ReadWrite);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ReadScalarAsync<T>(string path, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(path, SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<DatabaseValues> ReadValuesAsync(string path)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            path,
            SqliteOpenMode.ReadOnly);
        var metadata = new List<(string Key, string? Value)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key COLLATE BINARY;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                metadata.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        var internalIds = new List<long>();
        var pages = new List<(string, string, string, string, long, string, string)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT InternalId, PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text
                FROM Pages ORDER BY PageId COLLATE BINARY;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                internalIds.Add(reader.GetInt64(0));
                pages.Add((
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetString(6),
                    reader.GetString(7)));
            }
        }
        internalIds.Sort();
        return new DatabaseValues(metadata, pages, internalIds);
    }

    private static string Describe(ArchiveV1RestoreResult result) =>
        result.Error is null ? "success" : result.Error.Code.ToString();

    private static string Describe(ArchiveV1BackupResult result) =>
        result.Error is null ? "success" : result.Error.Code.ToString();

    private sealed record DatabaseValues(
        IReadOnlyList<(string Key, string? Value)> Metadata,
        IReadOnlyList<(string, string, string, string, long, string, string)> Pages,
        IReadOnlyList<long> InternalIds);

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

    private sealed class TrackingNonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        internal TrackingNonSeekableStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes, writable: false);
        }

        internal bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        internal bool ReadAttempted { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("Input must not be read.");
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("Input must not be read.");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingClock : IClock, IDisposable
    {
        private readonly DateTimeOffset _utcNow;
        private readonly ManualResetEventSlim _read = new();
        private readonly ManualResetEventSlim _continue = new();

        internal BlockingClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                _read.Set();
                _continue.Wait(TimeSpan.FromSeconds(30));
                return _utcNow;
            }
        }

        internal bool WaitUntilRead(TimeSpan timeout) => _read.Wait(timeout);

        internal void Continue() => _continue.Set();

        public void Dispose()
        {
            _continue.Set();
            _read.Dispose();
            _continue.Dispose();
        }
    }
}
