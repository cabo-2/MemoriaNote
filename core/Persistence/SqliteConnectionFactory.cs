using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence;

/// <summary>Creates consistently configured, non-pooled SQLite file connections.</summary>
internal static class SqliteConnectionFactory
{
    /// <summary>Creates a connection without opening it.</summary>
    internal static SqliteConnection Create(string databasePath, SqliteOpenMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = mode,
            Pooling = false
        }.ToString());
    }

    /// <summary>Creates and opens a connection.</summary>
    internal static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        SqliteOpenMode mode,
        CancellationToken cancellationToken = default)
    {
        var connection = Create(databasePath, mode);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
