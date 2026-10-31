using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies layered, read-only Schema V2 database validation.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2DatabaseValidatorTests
{
    /// <summary>Verifies a database from the product creator passes every layer.</summary>
    [Test]
    public async Task ValidateAsync_CreatedDatabase_PassesEveryLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("valid.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "valid", "Valid");

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        AssertValid(result);
    }

    /// <summary>Verifies identity failures remain separate from schema validity.</summary>
    [Test]
    public async Task ValidateAsync_ConflictingIdentity_ReportsIdentityLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("identity.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "identity", "Identity");
        await ExecuteAsync(
            databasePath,
            "UPDATE Metadata SET Value = '3' WHERE Key = 'Version';");

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Identity.Issues, Has.Some.Contains("Metadata.Version differs"));
            Assert.That(result.Schema.IsValid, Is.True);
            Assert.That(result.Integrity.IsValid, Is.True);
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("Metadata key 'Version' differs"));
        }
    }

    /// <summary>Verifies recognition and complete schema validation remain distinct.</summary>
    [Test]
    public async Task ValidateAsync_CurrentIdentityWithSchemaDefect_ReportsSchemaLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("schema.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "schema", "Schema");
        await ExecuteAsync(databasePath, "DROP INDEX IX_Pages_Name;");

        var recognition = await SqliteNotebookFormatRecognizer.RecognizeAsync(databasePath);
        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recognition.Classification,
                Is.EqualTo(NotebookFormatClassification.Current));
            Assert.That(result.Identity.IsValid, Is.True);
            Assert.That(result.Schema.Issues, Has.Some.Contains("IX_Pages_Name"));
            Assert.That(result.Integrity.IsValid, Is.True);
            Assert.That(result.AuthoritativeValues.WasEvaluated, Is.False);
        }
    }

    /// <summary>Verifies trigger semantics are checked beyond the object inventory.</summary>
    [Test]
    public async Task ValidateAsync_BroadFtsUpdateTrigger_ReportsRequiredSqlDifference()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("trigger.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "trigger", "Trigger");
        await ExecuteAsync(
            databasePath,
            """
            DROP TRIGGER Pages_FtsUpdate;
            CREATE TRIGGER Pages_FtsUpdate
            AFTER UPDATE ON Pages
            BEGIN
                INSERT INTO FtsIndexUnicode61(FtsIndexUnicode61, rowid, Name, Text)
                    VALUES('delete', old.InternalId, old.Name, old.Text);
                INSERT INTO FtsIndexUnicode61(rowid, Name, Text)
                    VALUES(new.InternalId, new.Name, new.Text);
                INSERT INTO FtsIndexTrigram(FtsIndexTrigram, rowid, Name, Text)
                    VALUES('delete', old.InternalId, old.Name, old.Text);
                INSERT INTO FtsIndexTrigram(rowid, Name, Text)
                    VALUES(new.InternalId, new.Name, new.Text);
            END;
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        Assert.That(result.Schema.Issues,
            Has.Some.Contains("AFTER UPDATE OF Name, Text"));
    }

    /// <summary>Verifies every FTS synchronization target is part of the trigger contract.</summary>
    [Test]
    public async Task ValidateAsync_InsertTriggerMissingTrigram_ReportsSchemaLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("trigger-target.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "trigger-target", "Trigger target");
        await ExecuteAsync(
            databasePath,
            """
            DROP TRIGGER Pages_FtsInsert;
            CREATE TRIGGER Pages_FtsInsert
            AFTER INSERT ON Pages
            BEGIN
                INSERT INTO FtsIndexUnicode61(rowid, Name, Text)
                    VALUES(new.InternalId, new.Name, new.Text);
            END;
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        Assert.That(result.Schema.Issues,
            Has.Some.Contains("INSERT INTO FtsIndexTrigram"));
    }

    /// <summary>Verifies each FTS5 tokenizer remains part of the schema contract.</summary>
    [Test]
    public async Task ValidateAsync_TrigramTableWithWrongTokenizer_ReportsSchemaLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("trigram-tokenizer.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "trigram", "Trigram");
        await ExecuteAsync(
            databasePath,
            """
            DROP TABLE FtsIndexTrigram;
            CREATE VIRTUAL TABLE FtsIndexTrigram USING fts5(
                Name,
                Text,
                content='Pages',
                content_rowid='InternalId',
                tokenize='unicode61');
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        Assert.That(result.Schema.Issues,
            Has.Some.Contains("FTS5 tokenizer does not match 'tokenize='trigram''"));
    }

    /// <summary>Verifies malformed authoritative table shape is reported rather than queried blindly.</summary>
    [Test]
    public async Task ValidateAsync_MalformedMetadataTable_ReportsIdentityAndSchema()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("metadata-shape.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "metadata", "Metadata");
        await ExecuteAsync(
            databasePath,
            "DROP TABLE Metadata; CREATE TABLE Metadata (Other TEXT NULL);");

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Identity.Issues,
                Has.Some.Contains("Metadata.Version must be present"));
            Assert.That(result.Schema.Issues, Has.Some.Contains("Metadata columns differ"));
            Assert.That(result.AuthoritativeValues.WasEvaluated, Is.False);
        }
    }

    /// <summary>Verifies foreign-key violations are owned by the SQLite integrity layer.</summary>
    [Test]
    public async Task ValidateAsync_ForeignKeyViolation_ReportsIntegrityLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("foreign-key.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "foreign-key", "Foreign key");
        await ExecuteAsync(
            databasePath,
            """
            PRAGMA foreign_keys = OFF;
            INSERT INTO PageTags(PageInternalId, Label) VALUES (999, 'orphan');
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Identity.IsValid, Is.True);
            Assert.That(result.Schema.IsValid, Is.True);
            Assert.That(result.Integrity.Issues,
                Has.Some.Contains("Foreign key violation in 'PageTags'"));
            Assert.That(result.AuthoritativeValues.WasEvaluated, Is.False);
        }
    }

    /// <summary>Verifies values beyond SQLite CHECK constraints are validated explicitly.</summary>
    [Test]
    public async Task ValidateAsync_InvalidAuthoritativeValues_ReportsValueLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("values.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "values", "Values");
        await ExecuteAsync(
            databasePath,
            """
            UPDATE Metadata SET Value = NULL WHERE Key = 'Name';
            INSERT INTO Pages(
                PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'control' || char(1),
                '2026-99-99 99:99:99.9999999',
                '2026-99-99 99:99:99.9999999',
                1,
                '[ "tag" ]',
                'body');
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Identity.IsValid, Is.True);
            Assert.That(result.Schema.IsValid, Is.True);
            Assert.That(result.Integrity.IsValid, Is.True);
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("Required metadata key 'Name' has a null value"));
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("control character in Name"));
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("invalid canonical CreateTime"));
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("invalid canonical UpdateTime"));
            Assert.That(result.AuthoritativeValues.Issues,
                Has.Some.Contains("Pages.Tags is not canonical JSON"));
            Assert.That(result.DerivedData.WasEvaluated, Is.False);
        }
    }

    /// <summary>Verifies derived mismatches are classified separately from schema validity.</summary>
    [Test]
    public async Task ValidateAsync_DerivedMismatches_ReportsDerivedDataLayer()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("derived.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "derived", "Derived");
        await ExecuteAsync(
            databasePath,
            """
            INSERT INTO Pages(
                PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text)
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'derived',
                '2026-10-31 00:00:00.0000000',
                '2026-10-31 00:00:00.0000000',
                1,
                '["expected"]',
                'searchable body');
            DELETE FROM PageTags WHERE Label = 'expected';
            INSERT INTO PageTags(PageInternalId, Label)
                SELECT InternalId, 'unexpected' FROM Pages;
            INSERT INTO FtsIndexTrigram(FtsIndexTrigram) VALUES('delete-all');
            """);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Identity.IsValid, Is.True);
            Assert.That(result.Schema.IsValid, Is.True);
            Assert.That(result.Integrity.IsValid, Is.True);
            Assert.That(result.AuthoritativeValues.IsValid, Is.True);
            Assert.That(result.DerivedData.IsValid, Is.False);
            Assert.That(result.DerivedData.Issues, Has.Some.Matches<SchemaV2DerivedDataIssue>(
                issue => issue is not null &&
                         issue.Index == SchemaV2DerivedIndexKind.PageTags &&
                         issue.Kind == SchemaV2DerivedDataIssueKind.MissingRows));
            Assert.That(result.DerivedData.Issues, Has.Some.Matches<SchemaV2DerivedDataIssue>(
                issue => issue is not null &&
                         issue.Index == SchemaV2DerivedIndexKind.PageTags &&
                         issue.Kind == SchemaV2DerivedDataIssueKind.UnexpectedRows));
            Assert.That(result.DerivedData.Issues, Has.Some.Matches<SchemaV2DerivedDataIssue>(
                issue => issue is not null &&
                         issue.Index == SchemaV2DerivedIndexKind.FtsTrigram &&
                         issue.Kind == SchemaV2DerivedDataIssueKind.ContentMismatch));
        }
    }

    /// <summary>Verifies validation does not change bytes or leave SQLite sidecars.</summary>
    [Test]
    public async Task ValidateAsync_ExistingDatabase_LeavesFileSetUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("unchanged.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "unchanged", "Unchanged");
        var beforeBytes = await File.ReadAllBytesAsync(databasePath);
        var beforeFiles = Directory.GetFileSystemEntries(directory.Path);

        var result = await SchemaV2DatabaseValidator.ValidateAsync(databasePath);

        AssertValid(result);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(databasePath), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies pre-cancellation avoids opening or changing the database.</summary>
    [Test]
    public async Task ValidateAsync_PreCancelled_LeavesDatabaseUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "cancelled", "Cancelled");
        var beforeBytes = await File.ReadAllBytesAsync(databasePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await SchemaV2DatabaseValidator.ValidateAsync(
                databasePath,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await File.ReadAllBytesAsync(databasePath), Is.EqualTo(beforeBytes));
    }

    private static void AssertValid(SchemaV2DatabaseValidation result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Identity.IsValid, Is.True);
            Assert.That(result.Schema.IsValid, Is.True);
            Assert.That(result.Integrity.IsValid, Is.True);
            Assert.That(result.AuthoritativeValues.IsValid, Is.True);
            Assert.That(result.DerivedData.IsValid, Is.True);
        }
    }

    private static async Task ExecuteAsync(string databasePath, string commandText)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadWrite);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
