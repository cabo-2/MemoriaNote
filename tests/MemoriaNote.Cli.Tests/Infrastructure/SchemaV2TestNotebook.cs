using MemoriaNote.Application;
using MemoriaNote.Domain;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace MemoriaNote.Cli.Tests.Infrastructure;

internal static class SchemaV2TestNotebook
{
    internal static async Task<NotebookPage> CreatePageAsync(
        string databasePath,
        string name,
        string text = "",
        IReadOnlyList<string>? tags = null)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        var result = await session.Pages.CreateAsync(
                new NotebookPageCreateRequest(
                    name,
                    text,
                    tags ?? Array.Empty<string>()),
                CancellationToken.None)
            .ConfigureAwait(false);
        return Require(result);
    }

    internal static async Task InsertPageAsync(
        string databasePath,
        Guid pageId,
        string name,
        string text)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Pages(PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES ($pageId, $name, $time, $time, 1, '[]', $text);
            """;
        command.Parameters.AddWithValue("$pageId", pageId.ToString("D"));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$time", "2026-01-01 00:00:00.0000000");
        command.Parameters.AddWithValue("$text", text);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static async Task SetPageTimesAsync(
        string databasePath,
        string name,
        DateTimeOffset createTimeUtc,
        DateTimeOffset updateTimeUtc)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Pages
            SET CreateTime = $createTime, UpdateTime = $updateTime
            WHERE Name = $name;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$createTime", FormatTime(createTimeUtc));
        command.Parameters.AddWithValue("$updateTime", FormatTime(updateTimeUtc));
        var changed = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException($"Expected one page named '{name}'.");
    }

    internal static async Task<NotebookPage?> FindByNameAsync(
        string databasePath,
        string name)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        var result = await session.Pages.GetByNameAsync(name, CancellationToken.None)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    internal static async Task<NotebookPage?> FindByIdAsync(
        string databasePath,
        PageId pageId)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        var result = await session.Pages.GetByIdAsync(pageId, CancellationToken.None)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    internal static async Task<IReadOnlyList<NotebookPageSummary>> ListAsync(
        string databasePath)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        var result = await session.Pages.QuerySummariesAsync(
                new NotebookPageQueryRequest(),
                CancellationToken.None)
            .ConfigureAwait(false);
        return Require(result);
    }

    internal static async Task<NotebookMetadataSnapshot> GetMetadataAsync(
        string databasePath)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        return Require(await session.Metadata.GetAsync(CancellationToken.None)
            .ConfigureAwait(false));
    }

    internal static async Task UpdateMetadataAsync(
        string databasePath,
        IReadOnlyDictionary<string, string?> changes)
    {
        await using var session = await OpenAsync(databasePath).ConfigureAwait(false);
        Require(await session.Metadata.UpdateAsync(
                new NotebookMetadataChangeSet(changes),
                CancellationToken.None)
            .ConfigureAwait(false));
    }

    internal static async Task SetUserVersionAsync(string databasePath, int version)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static async Task<(int ApplicationId, int UserVersion)> ReadIdentityAsync(
        string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return (
            await ReadPragmaAsync(connection, "application_id").ConfigureAwait(false),
            await ReadPragmaAsync(connection, "user_version").ConfigureAwait(false));
    }

    private static async Task<NotebookApplicationSession> OpenAsync(string databasePath)
    {
        var result = await NotebookApplication.OpenAsync(
                new NotebookOpenRequest(databasePath),
                CancellationToken.None)
            .ConfigureAwait(false);
        return Require(result);
    }

    private static T Require<T>(NotebookOperationResult<T> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Schema V2 test operation failed: {result.Failure!.Code}: " +
                result.Failure.Message);
        }
        return result.Value!;
    }

    private static async Task<int> ReadPragmaAsync(
        SqliteConnection connection,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture);
}
