using Microsoft.Data.Sqlite;

namespace MemoriaNote.Core.Tests.Infrastructure;

internal sealed class TemporaryNoteDatabase : IDisposable
{
    private bool _disposed;

    internal TemporaryNoteDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "note.db");

        Directory.CreateDirectory(DirectoryPath);
    }

    internal string DatabasePath { get; }

    internal string DirectoryPath { get; }

    internal Note CreateNote(string name, string title)
    {
        return Note.Create(name, title, DatabasePath);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        _disposed = true;
    }
}
