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
}
