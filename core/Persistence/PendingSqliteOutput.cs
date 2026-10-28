using System;
using System.IO;
using System.Linq;

namespace MemoriaNote.Persistence;

/// <summary>Owns a private SQLite file set until the completed database is published.</summary>
internal sealed class PendingSqliteOutput : IDisposable
{
    private bool _committed;
    private bool _disposed;

    private PendingSqliteOutput(
        string finalPath,
        string stagingDirectory,
        string stagingPath)
    {
        FinalPath = finalPath;
        StagingDirectory = stagingDirectory;
        StagingPath = stagingPath;
    }

    /// <summary>Gets the final output path.</summary>
    internal string FinalPath { get; }

    /// <summary>Gets the private path where the database is created.</summary>
    internal string StagingPath { get; }

    private string StagingDirectory { get; }

    /// <summary>Creates a private output beside a new final database path.</summary>
    internal static PendingSqliteOutput Create(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);

        var fullFinalPath = Path.GetFullPath(finalPath);
        ThrowIfFinalFileSetExists(fullFinalPath);

        var parent = Path.GetDirectoryName(fullFinalPath) ??
            throw new ArgumentException(
                "The output path must have a parent directory.",
                nameof(finalPath));
        Directory.CreateDirectory(parent);

        var fileName = Path.GetFileName(fullFinalPath);
        var stagingDirectory = Path.Combine(
            parent,
            $".mn-{fileName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        return new PendingSqliteOutput(
            fullFinalPath,
            stagingDirectory,
            Path.Combine(stagingDirectory, fileName));
    }

    /// <summary>Publishes a closed database that has no remaining sidecars.</summary>
    internal void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed)
            throw new InvalidOperationException("The SQLite output was already committed.");
        if (!File.Exists(StagingPath))
            throw new InvalidOperationException("The staging database does not exist.");

        var sidecars = SqliteDatabaseFiles.Enumerate(StagingPath)
            .Skip(1)
            .Where(File.Exists)
            .ToArray();
        if (sidecars.Length > 0)
        {
            throw new InvalidOperationException(
                "The staging database still has SQLite sidecar files and cannot be committed.");
        }

        ThrowIfFinalFileSetExists(FinalPath);

        File.Move(StagingPath, FinalPath, overwrite: false);
        _committed = true;
        Directory.Delete(StagingDirectory);
    }

    /// <summary>Deletes every uncommitted file owned by this output.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_committed)
            return;

        SqliteDatabaseFiles.Delete(StagingPath);
        if (Directory.Exists(StagingDirectory))
            Directory.Delete(StagingDirectory, recursive: true);
    }

    private static void ThrowIfFinalFileSetExists(string finalPath)
    {
        var existingPath = SqliteDatabaseFiles.Enumerate(finalPath)
            .FirstOrDefault(File.Exists);
        if (existingPath != null)
            throw new IOException($"The SQLite output already exists: {existingPath}");
    }
}
