using MemoriaNote.Cli.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies migration across the published CLI process boundary.</summary>
[TestFixture]
[Category("Process")]
public sealed class NotebookMigrationProcessTests
{
    /// <summary>Verifies a separate mn process migrates a schema v1 notebook.</summary>
    [Test]
    public async Task Migrate_ProcessBoundary_PublishesSchemaV2Notebook()
    {
        using var harness = new CliProcessHarness();
        var created = await harness.RunAsync("create", "legacy.mnote");
        Assert.That(created.ExitCode, Is.Zero, created.StandardError);
        var source = Path.Combine(harness.WorkingDirectory, "legacy.mnote");
        using (var connection = new SqliteConnection("Data Source=" + source))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Pages(
                    Uuid, Name, "Index", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased, Text)
                VALUES(
                    '11111111-1111-4111-8111-111111111111',
                    'Process page', 1, NULL, 'T',
                    '2026-01-01 00:00:00', '2026-01-01 00:00:00', 0, 'body');
                """;
            command.ExecuteNonQuery();
        }

        var result = await harness.RunAsync(
            "notebooks",
            "migrate",
            "legacy.mnote",
            "--output",
            "migrated.mnote",
            "--report-level",
            "info");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Migration completed"));
            Assert.That(result.StandardOutput, Does.Contain("migrated=1"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(
                File.Exists(Path.Combine(harness.WorkingDirectory, "migrated.mnote")),
                Is.True);
        }
    }
}
