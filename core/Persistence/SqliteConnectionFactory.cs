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

    /// <summary>Creates an immutable read-only connection for sidecar-free recognition.</summary>
    internal static SqliteConnection CreateImmutableReadOnly(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var dataSource = new Uri(fullPath).AbsoluteUri + "?immutable=1";
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
    }

    /// <summary>Creates and opens a connection.</summary>
    internal static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        SqliteOpenMode mode,
        CancellationToken cancellationToken = default)
    {
        return await OpenAsync(
                databasePath,
                mode,
                defaultTimeoutSeconds: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates and opens a connection with an explicit default timeout.</summary>
    internal static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        SqliteOpenMode mode,
        int defaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (defaultTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultTimeoutSeconds));
        return await OpenAsync(
                databasePath,
                mode,
                (int?)defaultTimeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        SqliteOpenMode mode,
        int? defaultTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var connection = Create(databasePath, mode);
        if (defaultTimeoutSeconds.HasValue)
            connection.DefaultTimeout = defaultTimeoutSeconds.Value;
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

    /// <summary>Creates and opens an immutable read-only recognition connection.</summary>
    internal static async Task<SqliteConnection> OpenImmutableReadOnlyAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        return await OpenImmutableReadOnlyAsync(
                databasePath,
                defaultTimeoutSeconds: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates and opens an immutable connection with an explicit default timeout.</summary>
    internal static async Task<SqliteConnection> OpenImmutableReadOnlyAsync(
        string databasePath,
        int defaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (defaultTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultTimeoutSeconds));
        return await OpenImmutableReadOnlyAsync(
                databasePath,
                (int?)defaultTimeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenImmutableReadOnlyAsync(
        string databasePath,
        int? defaultTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var connection = CreateImmutableReadOnly(databasePath);
        if (defaultTimeoutSeconds.HasValue)
            connection.DefaultTimeout = defaultTimeoutSeconds.Value;
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
