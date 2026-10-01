using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoriaNote.Archive;
using MemoriaNote.Core.Tests.Infrastructure;
using MemoriaNote.Models;
using MemoriaNote.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Locks down archive v1 compatibility across codec and service boundaries.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ArchiveV1CompatibilityTests
{
    const int LargeTextLength = 1024 * 1024;

    static readonly string ExamplesDirectory = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "reference",
        "archive",
        "v1",
        "examples");

    /// <summary>Verifies the standard writer keeps producing the fixed compatibility fixture.</summary>
    [Test]
    public async Task FullFieldFixture_MatchesCanonicalWriterOutput()
    {
        var metadata = ExpectedFixtureMetadata()
            .Select(row => new ArchiveV1MetadataRow(row.Key, row.Value))
            .ToArray();
        var pages = ExpectedFixturePages()
            .Select(row => new ArchiveV1PageRow(
                row.Rowid,
                row.Uuid,
                row.Name,
                row.Index,
                row.Tags,
                row.ContentType,
                row.CreateTime,
                row.UpdateTime,
                row.IsErased,
                row.Text))
            .ToArray();
        var request = new ArchiveV1WriteRequest(
            "1",
            new ArchiveV1Creator("MemoriaNote.Tests", "ncli-410"),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            metadata,
            ToAsync(pages));
        await using var output = new MemoryStream();

        await new ArchiveV1Writer().WriteAsync(output, request, CancellationToken.None);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entryName in new[] { "manifest.json", "metadata.json", "pages.ndjson" })
        {
            var entry = archive.GetEntry(entryName);
            Assert.That(entry, Is.Not.Null, entryName);
            using var input = entry!.Open();
            using var actual = new MemoryStream();
            input.CopyTo(actual);
            Assert.That(actual.ToArray(), Is.EqualTo(ReadFixture(entryName)), entryName);
        }
    }

    /// <summary>
    /// Verifies the fixed all-field fixture restores exact authoritative values and usable read models.
    /// </summary>
    [Test]
    public async Task FullFieldFixture_RestoresExactRowsAndReadModels()
    {
        using var database = new TemporaryNotebookDatabase();
        var destinationPath = Path.Combine(database.DirectoryPath, "fixture.mnote");
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var factory = CreateDatabaseFactory();
        var service = new ArchiveV1RestoreService(factory, temporaryFiles);
        await using var archive = CreateFullFieldFixtureArchive();

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(archive, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(result.MetadataCount, Is.EqualTo(8));
            Assert.That(result.PageCount, Is.EqualTo(3));
            Assert.That(ReadMetadata(destinationPath), Is.EqualTo(ExpectedFixtureMetadata()));
            Assert.That(ReadPages(destinationPath), Is.EqualTo(ExpectedFixturePages()));
        }

        var integrity = await new SqliteNotebookReadModelMaintenance(factory)
            .CheckIntegrityAsync(destinationPath, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(integrity.IsConsistent, Is.True);
            Assert.That(CountRows(destinationPath, "Contents"), Is.EqualTo(3));
            Assert.That(CountFtsMatches(destinationPath, "second"), Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies empty, single-page, and many-page databases preserve every raw value.
    /// </summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(64)]
    public async Task DatabaseScenarios_RoundTripEveryAuthoritativeValue(int pageCount)
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("compatibility", "Compatibility");
        SeedScenario(database.DatabasePath, pageCount);
        var sourceMetadata = ReadMetadata(database.DatabasePath);
        var sourcePages = ReadPages(database.DatabasePath);
        var archivePath = Path.Combine(database.DirectoryPath, $"scenario-{pageCount}.mnarchive");
        var destinationPath = Path.Combine(database.DirectoryPath, $"scenario-{pageCount}.mnote");
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var factory = CreateDatabaseFactory();
        var backup = CreateBackupService(factory, temporaryFiles);
        var restore = new ArchiveV1RestoreService(factory, temporaryFiles);

        var backupResult = await backup.BackupAsync(
            new ArchiveV1FileBackupRequest(database.DatabasePath, archivePath),
            CancellationToken.None);
        var restoreResult = await restore.RestoreAsync(
            new ArchiveV1FileRestoreRequest(archivePath, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupResult.IsSuccess, Is.True, Describe(backupResult));
            Assert.That(restoreResult.IsSuccess, Is.True, Describe(restoreResult));
            Assert.That(ReadMetadata(database.DatabasePath), Is.EqualTo(sourceMetadata));
            Assert.That(ReadPages(database.DatabasePath), Is.EqualTo(sourcePages));
            Assert.That(ReadMetadata(destinationPath), Is.EqualTo(sourceMetadata));
            Assert.That(ReadPages(destinationPath), Is.EqualTo(sourcePages));
            Assert.That(File.ReadAllBytes(database.DatabasePath), Is.Not.Empty);
            Assert.That(File.ReadAllBytes(archivePath), Is.Not.Empty);
        }

        var integrity = await new SqliteNotebookReadModelMaintenance(factory)
            .CheckIntegrityAsync(destinationPath, CancellationToken.None);
        Assert.That(integrity.IsConsistent, Is.True);
        if (pageCount > 0)
        {
            Assert.That(
                ReadPages(destinationPath)[^1].Text,
                Has.Length.EqualTo(pageCount == 64 ? LargeTextLength : 0));
        }
    }

    /// <summary>Verifies invalid archives are rejected before a restore database exists.</summary>
    [Test]
    public async Task InvalidFixtureMatrix_IsRejectedWithoutChangingInputOrOutput()
    {
        using var database = new TemporaryNotebookDatabase();
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var service = new ArchiveV1RestoreService(CreateDatabaseFactory(), temporaryFiles);
        var fixtures = CreateInvalidFixtureMatrix();

        foreach (var fixture in fixtures)
        {
            await using var archive = new MemoryStream(fixture.Bytes, writable: false);
            var before = SHA256.HashData(fixture.Bytes);
            var destinationPath = Path.Combine(database.DirectoryPath, fixture.Name + ".mnote");

            var result = await service.RestoreAsync(
                new ArchiveV1StreamRestoreRequest(archive, destinationPath),
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Error?.Code,
                    Is.EqualTo(ArchiveV1OperationErrorCode.ArchiveValidationFailed),
                    fixture.Name);
                Assert.That(
                    result.Error?.ValidationReport?.Issues.Any(issue =>
                        issue.Code == fixture.ExpectedIssue),
                    Is.True,
                    fixture.Name);
                Assert.That(File.Exists(destinationPath), Is.False, fixture.Name);
                Assert.That(SHA256.HashData(fixture.Bytes), Is.EqualTo(before), fixture.Name);
            }
        }

        Assert.That(ListOwnedTemporaryFiles(database.DirectoryPath), Is.Empty);
    }

    /// <summary>Verifies declared size and record limits are inclusive.</summary>
    [TestCase("metadata", "recordCount", "65536", ArchiveV1IssueCode.RecordCountMismatch)]
    [TestCase("metadata", "recordCount", "65537", ArchiveV1IssueCode.RecordLimitExceeded)]
    [TestCase("pages", "recordCount", "1000000", ArchiveV1IssueCode.RecordCountMismatch)]
    [TestCase("pages", "recordCount", "1000001", ArchiveV1IssueCode.RecordLimitExceeded)]
    [TestCase("metadata", "uncompressedByteLength", "16777216", ArchiveV1IssueCode.ByteLengthMismatch)]
    [TestCase("metadata", "uncompressedByteLength", "16777217", ArchiveV1IssueCode.EntryTooLarge)]
    [TestCase("pages", "uncompressedByteLength", "8589934592", ArchiveV1IssueCode.ByteLengthMismatch)]
    [TestCase("pages", "uncompressedByteLength", "8589934593", ArchiveV1IssueCode.EntryTooLarge)]
    public async Task ManifestResourceLimitBoundaries_ReturnStableIssue(
        string dataSet,
        string property,
        string value,
        ArchiveV1IssueCode expectedIssue)
    {
        var metadata = ReadFixture("metadata.json");
        var pages = ReadFixture("pages.ndjson");
        var manifest = Encoding.UTF8.GetString(ReadFixture("manifest.json"));
        var marker = dataSet == "metadata"
            ? "\"metadata\": {"
            : "\"pages\": {";
        var dataSetOffset = manifest.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(dataSetOffset, Is.GreaterThanOrEqualTo(0));
        var propertyOffset = manifest.IndexOf(
            $"\"{property}\": ",
            dataSetOffset,
            StringComparison.Ordinal);
        var valueStart = propertyOffset + property.Length + 4;
        var valueEnd = manifest.IndexOfAny(new[] { ',', '\n' }, valueStart);
        manifest = manifest[..valueStart] + value + manifest[valueEnd..];
        await using var archive = CreateZip(
            ("manifest.json", Encoding.UTF8.GetBytes(manifest)),
            ("metadata.json", metadata),
            ("pages.ndjson", pages));

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(
            report.Issues.Any(issue => issue.Code == expectedIssue),
            Is.True,
            Describe(report));
    }

    /// <summary>Verifies the complete legacy entry-layout classification matrix.</summary>
    [TestCaseSource(nameof(LegacyLayoutCases))]
    public async Task LegacyLayoutMatrix_ClassifiesWithoutInterpretingPayload(
        string[] entryNames,
        ArchiveV1Classification expectedClassification)
    {
        var entries = entryNames
            .Select(name => (name, new byte[] { 0xff, 0xfe, 0xfd }))
            .ToArray();
        await using var archive = CreateZip(entries);

        var report = await new ArchiveV1Reader().ValidateAsync(
            archive,
            CancellationToken.None);

        Assert.That(report.Classification, Is.EqualTo(expectedClassification));
        if (expectedClassification == ArchiveV1Classification.LegacyArchiveCandidate)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    report.Issues.Any(issue =>
                        issue.Code == ArchiveV1IssueCode.LegacyArchiveUnsupported),
                    Is.True);
                Assert.That(
                    report.Issues.Any(issue =>
                        issue.Code == ArchiveV1IssueCode.MalformedJson),
                    Is.False);
            }
        }
    }

    /// <summary>
    /// Verifies restore exposes the stable classification needed for manual-recovery guidance.
    /// </summary>
    [Test]
    public async Task LegacyCandidate_RestoreReturnsManualRecoveryClassification()
    {
        using var database = new TemporaryNotebookDatabase();
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var service = new ArchiveV1RestoreService(CreateDatabaseFactory(), temporaryFiles);
        await using var archive = CreateZip(
            ("metadata.json", new byte[] { 0xff }),
            ("001.json", new byte[] { 0xff }));
        var destinationPath = Path.Combine(database.DirectoryPath, "legacy.mnote");

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(archive, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Error?.Code,
                Is.EqualTo(ArchiveV1OperationErrorCode.ArchiveValidationFailed));
            Assert.That(
                result.Error?.ValidationReport?.Classification,
                Is.EqualTo(ArchiveV1Classification.LegacyArchiveCandidate));
            Assert.That(
                result.Error?.ValidationReport?.Issues.Any(issue =>
                    issue.Code == ArchiveV1IssueCode.LegacyArchiveUnsupported),
                Is.True);
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListOwnedTemporaryFiles(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies temporary-store failures preserve all caller-owned state.</summary>
    [Test]
    public async Task TemporaryStoreFailures_AreNonDestructive()
    {
        using var database = new TemporaryNotebookDatabase();
        database.CreateNotebook("failure", "Failure");
        var sourceBefore = SHA256.HashData(File.ReadAllBytes(database.DatabasePath));
        using var store = new ThrowingTemporaryFileStore();
        var factory = CreateDatabaseFactory();
        var backup = CreateBackupService(factory, store);
        var restore = new ArchiveV1RestoreService(factory, store);
        using var backupOutput = new MemoryStream();
        await using var restoreInput = CreateFullFieldFixtureArchive();
        var restoreBytes = restoreInput.ToArray();
        var destinationPath = Path.Combine(database.DirectoryPath, "failure.mnote");

        var backupResult = await backup.BackupAsync(
            new ArchiveV1StreamBackupRequest(database.DatabasePath, backupOutput),
            CancellationToken.None);
        var restoreResult = await restore.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(restoreInput, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupResult.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.IoFailure));
            Assert.That(restoreResult.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.IoFailure));
            Assert.That(backupOutput.Length, Is.Zero);
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(
                SHA256.HashData(File.ReadAllBytes(database.DatabasePath)),
                Is.EqualTo(sourceBefore));
            Assert.That(restoreInput.ToArray(), Is.EqualTo(restoreBytes));
        }
    }

    /// <summary>Verifies cancellation while spooling removes only the owned temporary input.</summary>
    [Test]
    public void Restore_CanceledDuringSpool_CleansOwnedOutput()
    {
        using var database = new TemporaryNotebookDatabase();
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var service = new ArchiveV1RestoreService(CreateDatabaseFactory(), temporaryFiles);
        using var cancellation = new CancellationTokenSource();
        using var input = new CancelAfterReadStream(
            CreateFullFieldFixtureArchive().ToArray(),
            cancellation);
        var destinationPath = Path.Combine(database.DirectoryPath, "canceled-spool.mnote");

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await service.RestoreAsync(
                new ArchiveV1StreamRestoreRequest(input, destinationPath),
                cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListOwnedTemporaryFiles(database.DirectoryPath), Is.Empty);
        }
    }

    /// <summary>Verifies cancellation after restore staging begins removes the temporary database.</summary>
    [Test]
    public void Restore_CanceledAtDatabaseCreation_CleansDatabaseAndSidecars()
    {
        using var database = new TemporaryNotebookDatabase();
        using var cancellation = new CancellationTokenSource();
        var factory = new CancelAtTemporaryDatabaseFactory(
            CreateDatabaseFactory(),
            cancellation);
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var service = new ArchiveV1RestoreService(factory, temporaryFiles);
        using var input = CreateFullFieldFixtureArchive();
        var archiveBefore = input.ToArray();
        var destinationPath = Path.Combine(database.DirectoryPath, "canceled-db.mnote");

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await service.RestoreAsync(
                new ArchiveV1StreamRestoreRequest(input, destinationPath),
                cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CancellationWasTriggered, Is.True);
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListOwnedTemporaryFiles(database.DirectoryPath), Is.Empty);
            Assert.That(input.ToArray(), Is.EqualTo(archiveBefore));
        }
    }

    /// <summary>Verifies a database staging failure leaves the archive and destination intact.</summary>
    [Test]
    public async Task Restore_DatabaseCreationFailure_CleansDatabaseAndSidecars()
    {
        using var database = new TemporaryNotebookDatabase();
        var factory = new ThrowAtTemporaryDatabaseFactory(CreateDatabaseFactory());
        using var temporaryFiles = new TemporaryFileStore(
            Path.Combine(database.DirectoryPath, "service-temp"));
        var service = new ArchiveV1RestoreService(factory, temporaryFiles);
        using var input = CreateFullFieldFixtureArchive();
        var archiveBefore = input.ToArray();
        var destinationPath = Path.Combine(database.DirectoryPath, "failed-db.mnote");

        var result = await service.RestoreAsync(
            new ArchiveV1StreamRestoreRequest(input, destinationPath),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.FailureWasTriggered, Is.True);
            Assert.That(result.Error?.Code, Is.EqualTo(ArchiveV1OperationErrorCode.IoFailure));
            Assert.That(File.Exists(destinationPath), Is.False);
            Assert.That(ListOwnedTemporaryFiles(database.DirectoryPath), Is.Empty);
            Assert.That(input.ToArray(), Is.EqualTo(archiveBefore));
        }
    }

    static IEnumerable<TestCaseData> LegacyLayoutCases()
    {
        yield return LegacyCase(
            ArchiveV1Classification.LegacyArchiveCandidate,
            "metadata.json");
        yield return LegacyCase(
            ArchiveV1Classification.LegacyArchiveCandidate,
            "metadata.json",
            "0.json",
            "001.json",
            "42.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "Metadata.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "-1.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "+1.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "1.0.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "pages/1.json");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "readme.txt");
        yield return LegacyCase(
            ArchiveV1Classification.UnrecognizedZip,
            "metadata.json",
            "metadata.json");
        yield return LegacyCase(
            ArchiveV1Classification.ArchiveV1,
            "manifest.json",
            "metadata.json",
            "1.json");
    }

    static TestCaseData LegacyCase(
        ArchiveV1Classification classification,
        params string[] names)
    {
        return new TestCaseData(names, classification)
            .SetName($"LegacyLayout_{classification}_{string.Join('_', names)}");
    }

    static IReadOnlyList<InvalidFixture> CreateInvalidFixtureMatrix()
    {
        var metadata = ReadFixture("metadata.json");
        var pages = ReadFixture("pages.ndjson");
        var manifest = Encoding.UTF8.GetString(ReadFixture("manifest.json"));

        var malformedMetadata = "[{\"key\":\"Version\",\"value\":\"1\""u8.ToArray();
        var duplicateRowid = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(pages).Replace("\"rowid\":9", "\"rowid\":1"));
        var badChecksumManifest = manifest.Replace(
            "cc9ec9bcb258420686af3cad1e9ceb71c839f34a2ca22d4ef6cb5efa65a494a1",
            "0c9ec9bcb258420686af3cad1e9ceb71c839f34a2ca22d4ef6cb5efa65a494a1");

        return new[]
        {
            new InvalidFixture(
                "malformed-json",
                CreateZipBytes(
                    CreateManifest(malformedMetadata, 1, pages, 3),
                    malformedMetadata,
                    pages),
                ArchiveV1IssueCode.MalformedJson),
            new InvalidFixture(
                "unsupported-version",
                CreateZipBytes(
                    Encoding.UTF8.GetBytes(manifest.Replace(
                        "\"contentModelVersion\": 1",
                        "\"contentModelVersion\": 2")),
                    metadata,
                    pages),
                ArchiveV1IssueCode.UnsupportedVersion),
            new InvalidFixture(
                "checksum-mismatch",
                CreateZipBytes(Encoding.UTF8.GetBytes(badChecksumManifest), metadata, pages),
                ArchiveV1IssueCode.ChecksumMismatch),
            new InvalidFixture(
                "duplicate-rowid",
                CreateZipBytes(
                    CreateManifest(metadata, 8, duplicateRowid, 3),
                    metadata,
                    duplicateRowid),
                ArchiveV1IssueCode.DuplicatePageRowId)
        };
    }

    static byte[] CreateManifest(
        byte[] metadata,
        int metadataCount,
        byte[] pages,
        int pageCount)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                format = "memoria-note-archive",
                archiveFormatVersion = 1,
                contentModelVersion = 1,
                sourceNotebookFormatVersion = "1",
                createdBy = new
                {
                    application = "MemoriaNote.Tests",
                    version = "ncli-410"
                },
                createdAtUtc = "2026-10-01T00:00:00.0000000Z",
                dataSets = new
                {
                    metadata = new
                    {
                        entry = "metadata.json",
                        recordCount = metadataCount,
                        uncompressedByteLength = metadata.LongLength,
                        sha256 = Convert.ToHexString(SHA256.HashData(metadata)).ToLowerInvariant()
                    },
                    pages = new
                    {
                        entry = "pages.ndjson",
                        recordCount = pageCount,
                        uncompressedByteLength = pages.LongLength,
                        sha256 = Convert.ToHexString(SHA256.HashData(pages)).ToLowerInvariant()
                    }
                }
            });
    }

    static ArchiveV1BackupService CreateBackupService(
        INotebookDbContextFactory factory,
        ITemporaryFileStore temporaryFiles)
    {
        return new ArchiveV1BackupService(
            factory,
            temporaryFiles,
            new FixedClock(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            new ArchiveV1Creator("MemoriaNote.Tests", "ncli-410"));
    }

    static SqliteNotebookDbContextFactory CreateDatabaseFactory()
    {
        return new SqliteNotebookDbContextFactory(NullLoggerFactory.Instance);
    }

    static void SeedScenario(string databasePath, int pageCount)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = @"
                INSERT INTO Metadata(Key, Value) VALUES('NCLI-410-Unknown', '  生の値  ');
                INSERT INTO Metadata(Key, Value) VALUES('NCLI-410-Null', NULL);
                INSERT INTO Metadata(Key, Value) VALUES('NCLI-410-Empty', '');";
            metadata.ExecuteNonQuery();
        }

        using var page = connection.CreateCommand();
        page.Transaction = transaction;
        page.CommandText = @"
            INSERT INTO Pages(
                Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text)
            VALUES(
                $rowid, $uuid, $name, $index, $tags, $contentType,
                $createTime, $updateTime, $isErased, $text);";
        page.Parameters.Add("$rowid", SqliteType.Integer);
        page.Parameters.Add("$uuid", SqliteType.Text);
        page.Parameters.Add("$name", SqliteType.Text);
        page.Parameters.Add("$index", SqliteType.Integer);
        page.Parameters.Add("$tags", SqliteType.Text);
        page.Parameters.Add("$contentType", SqliteType.Text);
        page.Parameters.Add("$createTime", SqliteType.Text);
        page.Parameters.Add("$updateTime", SqliteType.Text);
        page.Parameters.Add("$isErased", SqliteType.Integer);
        page.Parameters.Add("$text", SqliteType.Text);
        page.Prepare();

        for (var index = 0; index < pageCount; index++)
        {
            var isLast = index == pageCount - 1;
            page.Parameters["$rowid"].Value = index + 1;
            page.Parameters["$uuid"].Value = GuidFromIndex(index);
            page.Parameters["$name"].Value = index % 2 == 0 ? "同名" : "e\u0301";
            page.Parameters["$index"].Value = (index / 2) + 1;
            page.Parameters["$tags"].Value = index % 3 == 0
                ? " { \"dir\" : \"日本/雪\" } "
                : DBNull.Value;
            page.Parameters["$contentType"].Value = index % 2 == 0 ? "T" : "";
            page.Parameters["$createTime"].Value = "2026-10-01 01:02:03";
            page.Parameters["$updateTime"].Value = "2026-10-01 01:02:03.1234000";
            page.Parameters["$isErased"].Value = index % 2;
            page.Parameters["$text"].Value = isLast && pageCount == 64
                ? new string('大', LargeTextLength)
                : "";
            page.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    static string GuidFromIndex(int index)
    {
        return $"00000000-0000-4000-8000-{index + 1:000000000000}";
    }

    static async IAsyncEnumerable<ArchiveV1PageRow> ToAsync(
        IEnumerable<ArchiveV1PageRow> pages)
    {
        foreach (var page in pages)
        {
            await Task.Yield();
            yield return page;
        }
    }

    static IReadOnlyList<RawMetadataRow> ExpectedFixtureMetadata()
    {
        return new[]
        {
            new RawMetadataRow("CreateTime", "20261001123456"),
            new RawMetadataRow("EmptyValue", ""),
            new RawMetadataRow("Name", "compatibility"),
            new RawMetadataRow("NullValue", null),
            new RawMetadataRow("ReadOnly", "tRuE"),
            new RawMetadataRow("Title", "互換性 e\u0301"),
            new RawMetadataRow("Version", "1"),
            new RawMetadataRow("X-Unknown-雪", "  preserved  ")
        };
    }

    static IReadOnlyList<RawPageRow> ExpectedFixturePages()
    {
        return new[]
        {
            new RawPageRow(
                1,
                "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE",
                "同名",
                1,
                " \t",
                "",
                "2026-10-01 01:02:03",
                "2026-10-01 01:02:03.100",
                0,
                ""),
            new RawPageRow(
                9,
                "11111111-2222-4333-8444-555555555555",
                "同名",
                2,
                " { \"Dir\" : \"日本/雪\", \"Optional\" : null } ",
                null,
                "2026-10-02 02:03:04.1",
                "2026-10-02 02:03:04.1234567",
                1,
                "一行目\nsecond line\ne\u0301"),
            new RawPageRow(
                int.MaxValue,
                "99999999-8888-4777-8666-555555555555",
                null,
                int.MaxValue,
                null,
                "T",
                "2026-10-03 03:04:05",
                "2026-10-03 03:04:05",
                0,
                null)
        };
    }

    static IReadOnlyList<RawMetadataRow> ReadMetadata(string databasePath)
    {
        var rows = new List<RawMetadataRow>();
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawMetadataRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return rows;
    }

    static IReadOnlyList<RawPageRow> ReadPages(string databasePath)
    {
        var rows = new List<RawPageRow>();
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                CreateTime, UpdateTime, IsErased, Text
            FROM Pages
            ORDER BY Rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawPageRow(
                reader.GetInt32(0),
                reader.GetString(1),
                NullableString(reader, 2),
                reader.GetInt32(3),
                NullableString(reader, 4),
                NullableString(reader, 5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                NullableString(reader, 9)));
        }
        return rows;
    }

    static int CountRows(string databasePath, string tableName)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    static int CountFtsMatches(string databasePath, string query)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FtsIndex WHERE FtsIndex MATCH $query;";
        command.Parameters.AddWithValue("$query", query);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    static string? NullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    static MemoryStream CreateFullFieldFixtureArchive()
    {
        return CreateZip(
            ("manifest.json", ReadFixture("manifest.json")),
            ("metadata.json", ReadFixture("metadata.json")),
            ("pages.ndjson", ReadFixture("pages.ndjson")));
    }

    static byte[] ReadFixture(string fileName)
    {
        return File.ReadAllBytes(Path.Combine(
            ExamplesDirectory,
            "compatibility",
            "full-field",
            fileName));
    }

    static byte[] CreateZipBytes(byte[] manifest, byte[] metadata, byte[] pages)
    {
        using var archive = CreateZip(
            ("manifest.json", manifest),
            ("metadata.json", metadata),
            ("pages.ndjson", pages));
        return archive.ToArray();
    }

    static MemoryStream CreateZip(params (string Name, byte[] Bytes)[] entries)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(item.Bytes);
            }
        }
        output.Position = 0;
        return output;
    }

    static IReadOnlyList<string> ListOwnedTemporaryFiles(string rootDirectory)
    {
        return Directory
            .EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetFileName(path).Contains(".tmp", StringComparison.Ordinal) ||
                path.Contains("service-temp", StringComparison.Ordinal))
            .ToArray();
    }

    static string Describe(ArchiveV1ValidationReport report)
    {
        return string.Join(
            Environment.NewLine,
            report.Issues.Select(issue => $"{issue.Code}: {issue.Location}"));
    }

    static string Describe(ArchiveV1BackupResult result)
    {
        return result.Error == null ? "Success" : result.Error.Code.ToString();
    }

    static string Describe(ArchiveV1RestoreResult result)
    {
        if (result.Error?.ValidationReport != null)
            return Describe(result.Error.ValidationReport);
        return result.Error == null ? "Success" : result.Error.Code.ToString();
    }

    sealed record InvalidFixture(
        string Name,
        byte[] Bytes,
        ArchiveV1IssueCode ExpectedIssue);

    sealed record RawMetadataRow(string Key, string? Value);

    sealed record RawPageRow(
        int Rowid,
        string Uuid,
        string? Name,
        int Index,
        string? Tags,
        string? ContentType,
        string CreateTime,
        string UpdateTime,
        int IsErased,
        string? Text);

    sealed class ThrowingTemporaryFileStore : ITemporaryFileStore
    {
        public ITemporaryFile CreateFile(string? fileName = null)
        {
            throw new IOException("Injected temporary-file allocation failure.");
        }

        public void Dispose()
        {
        }
    }

    sealed class CancelAtTemporaryDatabaseFactory : INotebookDbContextFactory
    {
        readonly INotebookDbContextFactory _inner;
        readonly CancellationTokenSource _cancellation;

        internal CancelAtTemporaryDatabaseFactory(
            INotebookDbContextFactory inner,
            CancellationTokenSource cancellation)
        {
            _inner = inner;
            _cancellation = cancellation;
        }

        internal bool CancellationWasTriggered { get; private set; }

        public NotebookDbContext CreateDbContext(string databasePath)
        {
            var context = _inner.CreateDbContext(databasePath);
            if (Path.GetFileName(databasePath).Contains(".tmp", StringComparison.Ordinal))
            {
                CancellationWasTriggered = true;
                _cancellation.Cancel();
            }
            return context;
        }
    }

    sealed class ThrowAtTemporaryDatabaseFactory : INotebookDbContextFactory
    {
        readonly INotebookDbContextFactory _inner;

        internal ThrowAtTemporaryDatabaseFactory(INotebookDbContextFactory inner)
        {
            _inner = inner;
        }

        internal bool FailureWasTriggered { get; private set; }

        public NotebookDbContext CreateDbContext(string databasePath)
        {
            if (Path.GetFileName(databasePath).Contains(".tmp", StringComparison.Ordinal))
            {
                FailureWasTriggered = true;
                throw new IOException("Injected restore database creation failure.");
            }
            return _inner.CreateDbContext(databasePath);
        }
    }

    sealed class CancelAfterReadStream : Stream
    {
        readonly MemoryStream _inner;
        readonly CancellationTokenSource _cancellation;
        bool _readCompleted;

        internal CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation)
        {
            _inner = new MemoryStream(bytes, writable: false);
            _cancellation = cancellation;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readCompleted)
                throw new OperationCanceledException(_cancellation.Token);
            var read = _inner.Read(buffer, offset, Math.Min(count, 64));
            _readCompleted = true;
            _cancellation.Cancel();
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_readCompleted)
                return ValueTask.FromCanceled<int>(cancellationToken);
            var read = _inner.Read(buffer.Span[..Math.Min(buffer.Length, 64)]);
            _readCompleted = true;
            _cancellation.Cancel();
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
