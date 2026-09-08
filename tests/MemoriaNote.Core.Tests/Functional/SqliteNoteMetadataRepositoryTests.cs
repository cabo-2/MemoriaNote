using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies the metadata use-case contract implemented by the SQLite repository.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class SqliteNoteMetadataRepositoryTests
{
    readonly INoteMetadataRepository _repository =
        new SqliteNoteMetadataRepository(
            new SqliteNoteDatabaseFactory(NullLoggerFactory.Instance));

    /// <summary>
    /// Verifies that one update materializes all persisted values as a snapshot.
    /// </summary>
    [Test]
    public async Task UpdateAsync_PersistsAllRequestedValuesTogether()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        var createTime = new DateTime(2026, 1, 2, 9, 10, 11);
        var update = new NoteMetadataUpdate()
            .SetName("renamed-note")
            .SetTitle("Renamed Note")
            .SetVersion("updated-version")
            .SetDescription("Metadata description")
            .SetAuthor("Test Author")
            .SetReadOnly(true)
            .SetTag("baseline")
            .SetCreateTime(createTime);

        var result = await _repository.UpdateAsync(
            database.DatabasePath,
            update,
            CancellationToken.None);
        var reopened = await _repository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasIssues, Is.False);
            Assert.That(reopened.HasIssues, Is.False);
            Assert.That(reopened.Metadata.Name, Is.EqualTo("renamed-note"));
            Assert.That(reopened.Metadata.Title, Is.EqualTo("Renamed Note"));
            Assert.That(reopened.Metadata.Version, Is.EqualTo("updated-version"));
            Assert.That(reopened.Metadata.Description, Is.EqualTo("Metadata description"));
            Assert.That(reopened.Metadata.Author, Is.EqualTo("Test Author"));
            Assert.That(reopened.Metadata.ReadOnly, Is.True);
            Assert.That(reopened.Metadata.Tag, Is.EqualTo("baseline"));
            Assert.That(reopened.Metadata.CreateTime, Is.EqualTo(createTime));
        }

        using var context = new NoteDbContext(database.DatabasePath);
        Assert.That(
            context.Metadata.Find(NoteKeyValue.CreateTime)?.Value,
            Is.EqualTo("20260102091011"));
    }

    /// <summary>
    /// Verifies that a batch update uses one context and one asynchronous save.
    /// </summary>
    [Test]
    public async Task UpdateAsync_UsesOneContextAndOneSaveChangesAsync()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        var factory = new CountingDatabaseFactory();
        var repository = new SqliteNoteMetadataRepository(factory);

        await repository.UpdateAsync(
            database.DatabasePath,
            new NoteMetadataUpdate()
                .SetName("renamed-note")
                .SetTitle("Renamed Note"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.ContextCount, Is.EqualTo(1));
            Assert.That(factory.SaveChangesAsyncCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies that an existing 14-character time is parsed without guessing or rewriting it.
    /// </summary>
    [Test]
    public async Task LoadAsync_WithExistingCreateTime_LeavesStoredValueUnchanged()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using (var context = new NoteDbContext(database.DatabasePath))
        {
            context.Metadata.Add(new NoteKeyValue
            {
                Key = NoteKeyValue.CreateTime,
                Value = "20260102011011"
            });
            context.SaveChanges();
        }

        var result = await _repository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);

        using var verification = new NoteDbContext(database.DatabasePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasIssues, Is.False);
            Assert.That(
                result.Metadata.CreateTime,
                Is.EqualTo(new DateTime(2026, 1, 2, 1, 10, 11)));
            Assert.That(
                verification.Metadata.Find(NoteKeyValue.CreateTime)?.Value,
                Is.EqualTo("20260102011011"));
        }
    }

    /// <summary>
    /// Verifies that absent required keys and malformed stored values are classified.
    /// </summary>
    [Test]
    public async Task LoadAsync_WithMissingAndMalformedValues_ReturnsClassifiableIssues()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using (var context = new NoteDbContext(database.DatabasePath))
        {
            var title = context.Metadata.Find(NoteKeyValue.Title);
            context.Metadata.Remove(title!);
            context.Metadata.AddRange(
                new NoteKeyValue
                {
                    Key = NoteKeyValue.ReadOnly,
                    Value = "sometimes"
                },
                new NoteKeyValue
                {
                    Key = NoteKeyValue.CreateTime,
                    Value = "not-a-date"
                });
            context.SaveChanges();
        }

        var result = await _repository.LoadAsync(
            database.DatabasePath,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasIssues, Is.True);
            Assert.That(result.Metadata.Title, Is.Null);
            Assert.That(result.Metadata.ReadOnly, Is.False);
            Assert.That(result.Metadata.CreateTime, Is.EqualTo(default(DateTime)));
            Assert.That(
                result.Issues.Select(issue => (issue.Kind, issue.Key)),
                Is.EquivalentTo(new[]
                {
                    (MetadataLoadIssueKind.MissingKey, NoteKeyValue.Title),
                    (MetadataLoadIssueKind.InvalidBoolean, NoteKeyValue.ReadOnly),
                    (MetadataLoadIssueKind.InvalidCreateTime, NoteKeyValue.CreateTime)
                }));
        }
    }

    /// <summary>
    /// Verifies that a failed multi-field update leaves every original value intact.
    /// </summary>
    [Test]
    public void UpdateAsync_WhenOneRowFails_RollsBackEveryValue()
    {
        using var database = new TemporaryNoteDatabase();
        database.CreateNote("test-note", "Test Note");
        using (var context = new NoteDbContext(database.DatabasePath))
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TRIGGER Metadata_Update_Fail " +
                "BEFORE UPDATE ON Metadata " +
                "WHEN NEW.Key = 'Title' " +
                "BEGIN SELECT RAISE(ABORT, 'forced metadata failure'); END;");
        }

        var update = new NoteMetadataUpdate()
            .SetName("partially-renamed")
            .SetTitle("Rejected title");

        Assert.ThrowsAsync<DbUpdateException>(new Func<Task>(async () =>
            await _repository.UpdateAsync(
                database.DatabasePath,
                update,
                CancellationToken.None)));

        using var verification = new NoteDbContext(database.DatabasePath);
        var values = verification.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(values[NoteKeyValue.Name], Is.EqualTo("test-note"));
            Assert.That(values[NoteKeyValue.Title], Is.EqualTo("Test Note"));
        }
    }

    /// <summary>
    /// Verifies that cancellation is observed before a database is created or opened.
    /// </summary>
    [Test]
    public void LoadAsync_WithPreCancelledToken_DoesNotCreateDatabase()
    {
        using var database = new TemporaryNoteDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await _repository.LoadAsync(database.DatabasePath, cancellation.Token)));
        Assert.That(File.Exists(database.DatabasePath), Is.False);
    }

    sealed class CountingDatabaseFactory : INoteDatabaseFactory
    {
        internal int ContextCount { get; private set; }

        internal int SaveChangesAsyncCount { get; private set; }

        public NoteDbContext Create(string dataSource)
        {
            ContextCount++;
            var normalizedDataSource = Path.GetFullPath(dataSource);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = normalizedDataSource
            }.ToString();
            var options = new DbContextOptionsBuilder<NoteDbContext>()
                .UseSqlite(connectionString)
                .Options;
            return new CountingNoteDbContext(
                options,
                normalizedDataSource,
                () => SaveChangesAsyncCount++);
        }
    }

    sealed class CountingNoteDbContext : NoteDbContext
    {
        readonly Action _onSaveChangesAsync;

        internal CountingNoteDbContext(
            DbContextOptions<NoteDbContext> options,
            string dataSource,
            Action onSaveChangesAsync)
            : base(options)
        {
            DataSource = dataSource;
            _onSaveChangesAsync = onSaveChangesAsync;
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            _onSaveChangesAsync();
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
