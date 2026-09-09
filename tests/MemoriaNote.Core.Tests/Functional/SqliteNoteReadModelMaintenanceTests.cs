using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies read model inspection and reconstruction against real SQLite databases.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SqliteNoteReadModelMaintenanceTests
{
    readonly INoteDatabaseFactory _factory =
        new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance);

    /// <summary>
    /// Verifies that normal Page operations keep both read models consistent.
    /// </summary>
    [Test]
    public async Task CheckIntegrityAsync_AfterPageLifecycle_IsConsistent()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        await AssertConsistentAsync(database.DatabasePath, pageCount: 0);

        var first = note.CreatePage("Daily", "First searchable text");
        var second = note.CreatePage("Daily", "Second searchable text");
        await AssertConsistentAsync(database.DatabasePath, pageCount: 2);

        second.Name = "Archive";
        second.Text = "Updated searchable text";
        note.UpdatePage(second);
        await AssertConsistentAsync(database.DatabasePath, pageCount: 2);

        note.DeletePage(first.Guid);
        await AssertConsistentAsync(database.DatabasePath, pageCount: 1);
    }

    /// <summary>
    /// Verifies that count, identity, and summary-value problems are reported separately.
    /// </summary>
    [Test]
    public async Task CheckIntegrityAsync_WithContentCorruption_ReportsAffectedRows()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var missing = note.CreatePage("Missing", "First text");
        var mismatched = note.CreatePage("Mismatched", "Second text");
        note.CreatePage("Unchanged", "Third text");
        var unexpectedUuid = Guid.NewGuid().ToString("D");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM Contents WHERE Uuid = {missing.Uuid};");
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Contents
                SET Rowid = 999, Name = 'Corrupt name'
                WHERE Uuid = {mismatched.Uuid};");
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO Contents(
                    Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased)
                SELECT
                    999, {unexpectedUuid}, Name, ""Index"", Tags, ContentType,
                    CreateTime, UpdateTime, IsErased
                FROM Pages
                WHERE Uuid = {missing.Uuid};");
        }

        var report = await CreateMaintenance().CheckIntegrityAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.False);
            Assert.That(report.PageCount, Is.EqualTo(3));
            Assert.That(report.ContentCount, Is.EqualTo(3));
            Assert.That(report.FtsIndexIsConsistent, Is.True);
            Assert.That(report.MissingTriggers, Is.Empty);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.MissingContent &&
                    issue.Uuid == missing.Uuid),
                Is.True);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.UnexpectedContent &&
                    issue.Uuid == unexpectedUuid),
                Is.True);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.DuplicateRowId &&
                    issue.RowId == 999),
                Is.True);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.IdentityMismatch &&
                    issue.Uuid == mismatched.Uuid),
                Is.True);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.ValueMismatch &&
                    issue.Uuid == mismatched.Uuid &&
                    issue.PropertyName == "Name" &&
                    issue.ExpectedValue == "Mismatched" &&
                    issue.ActualValue == "Corrupt name"),
                Is.True);
        }
    }

    /// <summary>
    /// Verifies that duplicate UUIDs are detected even if the table constraint is damaged.
    /// </summary>
    [Test]
    public async Task CheckIntegrityAsync_WithDuplicateContentUuid_ReportsDuplicate()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Entry", "Searchable text");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE Contents;");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE Contents (
                    Uuid TEXT NOT NULL,
                    Rowid INTEGER NOT NULL,
                    Name TEXT NULL,
                    ""Index"" INTEGER NOT NULL,
                    Tags TEXT NULL,
                    ContentType TEXT NULL,
                    CreateTime TEXT NOT NULL,
                    UpdateTime TEXT NOT NULL,
                    IsErased INTEGER NOT NULL);");
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO Contents
                SELECT Uuid, Rowid, Name, ""Index"", Tags, ContentType,
                       CreateTime, UpdateTime, IsErased
                FROM Pages WHERE Uuid = {page.Uuid};");
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO Contents
                SELECT Uuid, Rowid + 100, Name, ""Index"", Tags, ContentType,
                       CreateTime, UpdateTime, IsErased
                FROM Pages WHERE Uuid = {page.Uuid};");
        }

        var report = await CreateMaintenance().CheckIntegrityAsync(
            database.DatabasePath,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue =>
                issue.Kind == ReadModelIntegrityIssueKind.DuplicateUuid &&
                issue.Uuid == page.Uuid),
            Is.True);
    }

    /// <summary>
    /// Verifies that each required synchronization trigger is reported when absent.
    /// </summary>
    [TestCase("Pages_Insert")]
    [TestCase("Pages_Update")]
    [TestCase("Pages_Delete")]
    public async Task CheckIntegrityAsync_WhenTriggerIsMissing_ReportsTrigger(string triggerName)
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlRawAsync($"DROP TRIGGER {triggerName};");
        }

        var report = await CreateMaintenance().CheckIntegrityAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.False);
            Assert.That(report.MissingTriggers, Is.EqualTo(new[] { triggerName }));
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.MissingTrigger &&
                    issue.PropertyName == triggerName),
                Is.True);
        }
    }

    /// <summary>
    /// Verifies that FTS5 detects a mismatch between its index and Pages.
    /// </summary>
    [Test]
    public async Task CheckIntegrityAsync_WithFtsIndexCorruption_ReportsMismatch()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Entry", "Searchable text");
        await RemoveFromFtsIndexAsync(database.DatabasePath, page);

        var report = await CreateMaintenance().CheckIntegrityAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.False);
            Assert.That(report.FtsIndexIsConsistent, Is.False);
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.FtsIndexMismatch),
                Is.True);
        }
    }

    /// <summary>
    /// Verifies that a missing FTS5 table is distinguished from index corruption.
    /// </summary>
    [Test]
    public async Task CheckIntegrityAsync_WhenFtsIndexIsMissing_ReportsMissingTable()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE FtsIndex;");
        }

        var report = await CreateMaintenance().CheckIntegrityAsync(
            database.DatabasePath,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue =>
                issue.Kind == ReadModelIntegrityIssueKind.MissingFtsIndex),
            Is.True);
    }

    /// <summary>
    /// Verifies reconstruction repairs both read models and restores search behavior.
    /// </summary>
    [Test]
    public async Task RebuildAsync_WithCorruptReadModels_RepairsIntegrityAndSearch()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Rebuild Target", "A quasar needs indexing.");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM Contents WHERE Uuid = {page.Uuid};");
        }
        await RemoveFromFtsIndexAsync(database.DatabasePath, page);

        var report = await CreateMaintenance().RebuildAsync(
            database.DatabasePath,
            CancellationToken.None);
        var heading = await note.SearchAsync(
            "Rebuild Target",
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);
        var fullText = await note.SearchAsync(
            "quasar",
            SearchMethodType.FullText,
            0,
            10,
            CancellationToken.None);
        var empty = await note.SearchAsync(
            string.Empty,
            SearchMethodType.Heading,
            0,
            10,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.True);
            Assert.That(report.PageCount, Is.EqualTo(1));
            Assert.That(report.ContentCount, Is.EqualTo(1));
            Assert.That(heading.Contents.Single().Guid, Is.EqualTo(page.Guid));
            Assert.That(fullText.Contents.Single().Guid, Is.EqualTo(page.Guid));
            Assert.That(empty.Contents.Single().Guid, Is.EqualTo(page.Guid));
        }
    }

    /// <summary>
    /// Verifies that reconstruction rolls back if required triggers remain absent.
    /// </summary>
    [Test]
    public async Task RebuildAsync_WhenPostCheckFails_RollsBackContentsChanges()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Entry", "Searchable text");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM Contents WHERE Uuid = {page.Uuid};");
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER Pages_Insert;");
        }

        var report = await CreateMaintenance().RebuildAsync(
            database.DatabasePath,
            CancellationToken.None);

        using var verification = _factory.Create(database.DatabasePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.False);
            Assert.That(report.MissingTriggers, Is.EqualTo(new[] { "Pages_Insert" }));
            Assert.That(
                report.Issues.Any(issue =>
                    issue.Kind == ReadModelIntegrityIssueKind.MissingContent &&
                    issue.Uuid == page.Uuid),
                Is.True);
            Assert.That(
                await verification.Contents.AnyAsync(content => content.Uuid == page.Uuid),
                Is.False);
        }
    }

    /// <summary>
    /// Verifies that an infrastructure failure during reconstruction rolls back earlier writes.
    /// </summary>
    [Test]
    public async Task RebuildAsync_WhenFtsRebuildFails_RollsBackContentsChanges()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Entry", "Searchable text");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Contents SET Name = 'Corrupt name'
                WHERE Uuid = {page.Uuid};");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE FtsIndex;");
        }

        Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(new Func<Task>(async () =>
            await CreateMaintenance().RebuildAsync(
                database.DatabasePath,
                CancellationToken.None)));

        using var verification = _factory.Create(database.DatabasePath);
        Assert.That(
            await verification.Contents
                .Where(content => content.Uuid == page.Uuid)
                .Select(content => content.Name)
                .SingleAsync(),
            Is.EqualTo("Corrupt name"));
    }

    /// <summary>
    /// Verifies that pre-cancellation does not change a corrupt read model.
    /// </summary>
    [Test]
    public async Task RebuildAsync_WhenPreCancelled_DoesNotChangeDatabase()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Entry", "Searchable text");
        using (var context = _factory.Create(database.DatabasePath))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM Contents WHERE Uuid = {page.Uuid};");
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await CreateMaintenance().RebuildAsync(
                database.DatabasePath,
                cancellation.Token)));

        using var verification = _factory.Create(database.DatabasePath);
        Assert.That(
            await verification.Contents.AnyAsync(content => content.Uuid == page.Uuid),
            Is.False);
    }

    private INoteReadModelMaintenance CreateMaintenance()
    {
        return new SqliteNoteReadModelMaintenance(_factory);
    }

    private async Task AssertConsistentAsync(string dataSource, int pageCount)
    {
        var report = await CreateMaintenance().CheckIntegrityAsync(
            dataSource,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.IsConsistent, Is.True);
            Assert.That(report.PageCount, Is.EqualTo(pageCount));
            Assert.That(report.ContentCount, Is.EqualTo(pageCount));
            Assert.That(report.FtsIndexIsConsistent, Is.True);
            Assert.That(report.MissingTriggers, Is.Empty);
            Assert.That(report.Issues, Is.Empty);
        }
    }

    private async Task RemoveFromFtsIndexAsync(string dataSource, Page page)
    {
        using var context = _factory.Create(dataSource);
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO FtsIndex(FtsIndex, Rowid, Uuid, Name, Tags, Text)
            VALUES('delete', {page.Rowid}, {page.Uuid}, {page.Name}, {page.Tags}, {page.Text});");
    }
}
