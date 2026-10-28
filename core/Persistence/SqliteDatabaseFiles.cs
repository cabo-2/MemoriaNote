using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MemoriaNote.Persistence;

/// <summary>Identifies and removes files owned by one SQLite database.</summary>
internal static class SqliteDatabaseFiles
{
    private static readonly string[] SidecarSuffixes = ["-journal", "-shm", "-wal"];

    /// <summary>Returns the database path followed by its possible sidecar paths.</summary>
    internal static IReadOnlyList<string> Enumerate(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        return new[] { fullPath }
            .Concat(SidecarSuffixes.Select(suffix => fullPath + suffix))
            .ToArray();
    }

    /// <summary>Returns whether the database or one of its sidecars exists.</summary>
    internal static bool AnyExists(string databasePath)
    {
        return Enumerate(databasePath).Any(File.Exists);
    }

    /// <summary>Deletes a database and any journal, shared-memory, or WAL sidecars.</summary>
    internal static void Delete(string databasePath)
    {
        foreach (var path in Enumerate(databasePath))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
