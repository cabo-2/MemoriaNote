using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.Persistence;

/// <summary>Classifies a main database header before SQLite opens the file.</summary>
internal enum SqliteNotebookHeaderCandidate
{
    SchemaV2,
    NewerSchemaV2,
    LegacyOrUnknown
}

/// <summary>Captures the SQLite header and file set used by format recognition.</summary>
internal sealed class SqliteNotebookRecognitionSnapshot
{
    private const int HeaderLength = 100;
    private const int WriteVersionOffset = 18;
    private const int ReadVersionOffset = 19;
    private const int UserVersionOffset = 60;
    private const int ApplicationIdOffset = 68;
    private const int SharedMemoryFileIndex = 2;
    private const int WalFileIndex = 3;

    private static readonly byte[] SqliteMagic =
        "SQLite format 3\0"u8.ToArray();

    private readonly byte[] _header;
    private readonly bool[] _filePresence;

    private SqliteNotebookRecognitionSnapshot(
        byte[] header,
        bool[] filePresence)
    {
        _header = header;
        _filePresence = filePresence;
        Candidate = Classify(header);
    }

    /// <summary>Gets the connection policy indicated by the main database header.</summary>
    internal SqliteNotebookHeaderCandidate Candidate { get; }

    /// <summary>Gets whether any rollback-journal, shared-memory, or WAL file exists.</summary>
    internal bool HasSidecars => _filePresence.Skip(1).Any(exists => exists);

    /// <summary>Gets whether the existing sidecars cannot form a read-only WAL snapshot.</summary>
    internal bool HasIncompleteWalPair
    {
        get
        {
            var hasSharedMemory = _filePresence[SharedMemoryFileIndex];
            var hasWal = _filePresence[WalFileIndex];
            if (hasSharedMemory != hasWal)
                return true;

            return HasSidecars && HeaderUsesWal() && !hasWal;
        }
    }

    /// <summary>Captures the first 100 bytes and known SQLite file-set members.</summary>
    internal static async Task<SqliteNotebookRecognitionSnapshot> CaptureAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(databasePath);
        var header = await ReadHeaderAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);
        var filePresence = SqliteDatabaseFiles.Enumerate(fullPath)
            .Select(File.Exists)
            .ToArray();
        return new SqliteNotebookRecognitionSnapshot(header, filePresence);
    }

    /// <summary>Gets whether the header and file-set membership are unchanged.</summary>
    internal bool HasSameSourceState(SqliteNotebookRecognitionSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _header.AsSpan().SequenceEqual(other._header) &&
            _filePresence.AsSpan().SequenceEqual(other._filePresence);
    }

    private static SqliteNotebookHeaderCandidate Classify(byte[] header)
    {
        if (header.Length < HeaderLength ||
            !header.AsSpan(0, SqliteMagic.Length).SequenceEqual(SqliteMagic))
        {
            return SqliteNotebookHeaderCandidate.LegacyOrUnknown;
        }

        var userVersion = BinaryPrimitives.ReadUInt32BigEndian(
            header.AsSpan(UserVersionOffset, sizeof(uint)));
        var applicationId = BinaryPrimitives.ReadUInt32BigEndian(
            header.AsSpan(ApplicationIdOffset, sizeof(uint)));
        if (applicationId != unchecked((uint)SchemaV2Format.ApplicationId))
            return SqliteNotebookHeaderCandidate.LegacyOrUnknown;
        if (userVersion == SchemaV2Format.UserVersion)
            return SqliteNotebookHeaderCandidate.SchemaV2;
        return userVersion > SchemaV2Format.UserVersion
            ? SqliteNotebookHeaderCandidate.NewerSchemaV2
            : SqliteNotebookHeaderCandidate.LegacyOrUnknown;
    }

    private bool HeaderUsesWal()
    {
        return _header.Length >= HeaderLength &&
            _header.AsSpan(0, SqliteMagic.Length).SequenceEqual(SqliteMagic) &&
            _header[WriteVersionOffset] == 2 &&
            _header[ReadVersionOffset] == 2;
    }

    private static async Task<byte[]> ReadHeaderAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        var buffer = new byte[HeaderLength];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            totalRead += read;
        }

        return totalRead == buffer.Length
            ? buffer
            : buffer.AsSpan(0, totalRead).ToArray();
    }
}

/// <summary>Reports that immutable recognition raced with a source change.</summary>
internal sealed class NotebookRecognitionConcurrentChangeException : IOException
{
    internal NotebookRecognitionConcurrentChangeException()
        : base("The notebook changed while its format was being recognized.")
    {
    }

    internal NotebookRecognitionConcurrentChangeException(Exception innerException)
        : base(
            "The notebook changed while its format was being recognized.",
            innerException)
    {
    }
}
