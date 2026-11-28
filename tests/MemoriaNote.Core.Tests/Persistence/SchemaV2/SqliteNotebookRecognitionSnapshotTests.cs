using System.Buffers.Binary;
using MemoriaNote.Persistence.SchemaV2;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies connection-free SQLite header and file-set preflight.</summary>
[TestFixture]
public sealed class SqliteNotebookRecognitionSnapshotTests
{
    /// <summary>Verifies schema v2 candidates use big-endian header fields.</summary>
    [TestCase(2u, (int)SqliteNotebookHeaderCandidate.SchemaV2)]
    [TestCase(3u, (int)SqliteNotebookHeaderCandidate.NewerSchemaV2)]
    [TestCase(1u, (int)SqliteNotebookHeaderCandidate.LegacyOrUnknown)]
    public async Task CaptureAsync_MemoriaNoteHeader_ClassifiesVersion(
        uint userVersion,
        int expected)
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("header.mnote");
        await File.WriteAllBytesAsync(
            databasePath,
            CreateHeader(unchecked((uint)SchemaV2Format.ApplicationId), userVersion));

        var snapshot = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        Assert.That(snapshot.Candidate, Is.EqualTo((SqliteNotebookHeaderCandidate)expected));
    }

    /// <summary>Verifies short and non-SQLite inputs are not positive candidates.</summary>
    [TestCase(20, true)]
    [TestCase(100, false)]
    public async Task CaptureAsync_InvalidHeader_IsLegacyOrUnknown(
        int length,
        bool includeMagic)
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("invalid.mnote");
        var bytes = new byte[length];
        if (includeMagic)
            "SQLite format 3\0"u8.CopyTo(bytes);
        await File.WriteAllBytesAsync(databasePath, bytes);

        var snapshot = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        Assert.That(
            snapshot.Candidate,
            Is.EqualTo(SqliteNotebookHeaderCandidate.LegacyOrUnknown));
    }

    /// <summary>Verifies another application's SQLite header is not a positive candidate.</summary>
    [Test]
    public async Task CaptureAsync_OtherApplicationId_IsLegacyOrUnknown()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("other.mnote");
        await File.WriteAllBytesAsync(databasePath, CreateHeader(0, 2));

        var snapshot = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        Assert.That(
            snapshot.Candidate,
            Is.EqualTo(SqliteNotebookHeaderCandidate.LegacyOrUnknown));
    }

    /// <summary>Verifies a WAL header with only a journal is not opened normally.</summary>
    [Test]
    public async Task CaptureAsync_WalHeaderWithJournalOnly_HasIncompleteWalPair()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("wal.mnote");
        await File.WriteAllBytesAsync(
            databasePath,
            CreateHeader(0, 0, useWriteAheadLogging: true));
        await File.WriteAllBytesAsync(databasePath + "-journal", []);

        var snapshot = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        Assert.That(snapshot.HasIncompleteWalPair, Is.True);
    }

    /// <summary>Verifies source-state comparison observes header changes.</summary>
    [Test]
    public async Task HasSameSourceState_HeaderChanges_ReturnsFalse()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("changed.mnote");
        await File.WriteAllBytesAsync(databasePath, CreateHeader(0, 0));
        var before = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);
        await File.WriteAllBytesAsync(databasePath, CreateHeader(0, 1));

        var after = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        Assert.That(before.HasSameSourceState(after), Is.False);
    }

    /// <summary>Verifies source-state comparison observes a new sidecar.</summary>
    [Test]
    public async Task HasSameSourceState_SidecarAppears_ReturnsFalse()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("changed.mnote");
        await File.WriteAllBytesAsync(databasePath, CreateHeader(0, 0));
        var before = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);
        await File.WriteAllBytesAsync(databasePath + "-wal", []);

        var after = await SqliteNotebookRecognitionSnapshot.CaptureAsync(
            databasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.HasSameSourceState(after), Is.False);
            Assert.That(after.HasIncompleteWalPair, Is.True);
        }
    }

    private static byte[] CreateHeader(
        uint applicationId,
        uint userVersion,
        bool useWriteAheadLogging = false)
    {
        var header = new byte[100];
        "SQLite format 3\0"u8.CopyTo(header);
        if (useWriteAheadLogging)
        {
            header[18] = 2;
            header[19] = 2;
        }
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(60, 4), userVersion);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(68, 4), applicationId);
        return header;
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
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
