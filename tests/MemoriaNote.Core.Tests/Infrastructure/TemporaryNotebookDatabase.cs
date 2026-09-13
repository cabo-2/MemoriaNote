using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoriaNote.Core.Tests.Infrastructure;

internal sealed class TemporaryNotebookDatabase : IDisposable
{
    private readonly INotebookMigrator _notebookMigrator;
    private bool _disposed;

    internal TemporaryNotebookDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "note.db");

        Directory.CreateDirectory(DirectoryPath);

        var databaseFactory =
            new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
        _notebookMigrator = new SqliteNotebookMigrator(
            databaseFactory,
            new SqliteNotebookMetadataRepository(databaseFactory));
    }

    internal string DatabasePath { get; }

    internal string DirectoryPath { get; }

    internal Notebook CreateNotebook(string name, string title)
    {
        return CreateNotebook(name, title, DatabasePath);
    }

    internal Notebook CreateNotebook(string name, string title, string databasePath)
    {
        return _notebookMigrator.CreateAsync(
                name,
                title,
                databasePath,
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
