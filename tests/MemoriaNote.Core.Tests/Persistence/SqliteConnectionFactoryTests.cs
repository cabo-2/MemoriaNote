using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence;

/// <summary>Verifies the shared direct-SQL connection configuration.</summary>
[TestFixture]
public sealed class SqliteConnectionFactoryTests
{
    /// <summary>Verifies connections use normalized file paths, explicit modes, and no pooling.</summary>
    [Test]
    public void Create_ConfiguresFileConnectionWithoutPooling()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"),
            "note.db");

        using var connection = SqliteConnectionFactory.Create(
            databasePath,
            SqliteOpenMode.ReadOnly);
        var settings = new SqliteConnectionStringBuilder(connection.ConnectionString);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.DataSource, Is.EqualTo(Path.GetFullPath(databasePath)));
            Assert.That(settings.Mode, Is.EqualTo(SqliteOpenMode.ReadOnly));
            Assert.That(settings.Pooling, Is.False);
        }
    }

    /// <summary>Verifies immutable recognition uses an escaped URI and remains non-pooled.</summary>
    [Test]
    public async Task CreateImmutableReadOnly_OpensEscapedFileUri()
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            "MemoriaNote.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var databasePath = Path.Combine(directoryPath, "日本語 # 100%.db");
        try
        {
            await using (var writable = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadWriteCreate))
            {
                await using var command = writable.CreateCommand();
                command.CommandText = "CREATE TABLE Example (Value INTEGER NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            await using var connection =
                SqliteConnectionFactory.CreateImmutableReadOnly(databasePath);
            var settings = new SqliteConnectionStringBuilder(connection.ConnectionString);
            await connection.OpenAsync();
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM Example;";

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Convert.ToInt64(await query.ExecuteScalarAsync()), Is.Zero);
                Assert.That(settings.DataSource, Does.StartWith("file:"));
                Assert.That(settings.DataSource, Does.EndWith("?immutable=1"));
                Assert.That(settings.Mode, Is.EqualTo(SqliteOpenMode.ReadOnly));
                Assert.That(settings.Pooling, Is.False);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }
}
