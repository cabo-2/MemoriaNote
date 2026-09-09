using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoriaNote.Core.Tests.Infrastructure;

internal sealed class TemporaryNoteDatabase : IDisposable
{
    private readonly INoteMigrator _noteMigrator;
    private bool _disposed;

    internal TemporaryNoteDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "note.db");

        Directory.CreateDirectory(DirectoryPath);

        var databaseFactory =
            new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance);
        _noteMigrator = new SqliteNoteMigrator(
            databaseFactory,
            new SqliteNoteMetadataRepository(databaseFactory));
    }

    internal string DatabasePath { get; }

    internal string DirectoryPath { get; }

    internal Note CreateNote(string name, string title)
    {
        return CreateNote(name, title, DatabasePath);
    }

    internal Note CreateNote(string name, string title, string dataSource)
    {
        return _noteMigrator.CreateAsync(
                name,
                title,
                dataSource,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
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
