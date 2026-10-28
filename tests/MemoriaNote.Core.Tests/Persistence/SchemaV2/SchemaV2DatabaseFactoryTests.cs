using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies Schema V2 database creation and safe publication.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2DatabaseFactoryTests
{
    /// <summary>Verifies creation persists format identity and required metadata.</summary>
    [Test]
    public async Task CreateAsync_CreatesFormatIdentityAndMetadata()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("created.mnote");

        await SchemaV2DatabaseFactory.CreateAsync(
            databasePath,
            "created-note",
            "Created Note");

        await using var connection = await SqliteConnectionFactory.OpenAsync(
            databasePath,
            SqliteOpenMode.ReadOnly);
        var applicationId = await ReadInt32Async(connection, "PRAGMA application_id;");
        var userVersion = await ReadInt32Async(connection, "PRAGMA user_version;");
        var metadata = await ReadMetadataAsync(connection);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(applicationId, Is.EqualTo(SchemaV2Format.ApplicationId));
            Assert.That(userVersion, Is.EqualTo(SchemaV2Format.UserVersion));
            Assert.That(metadata["Name"], Is.EqualTo("created-note"));
            Assert.That(metadata["Title"], Is.EqualTo("Created Note"));
            Assert.That(metadata["Version"], Is.EqualTo(SchemaV2Format.MetadataVersion));
            Assert.That(SqliteDatabaseFiles.AnyExists(databasePath), Is.True);
            Assert.That(
                Directory.GetFileSystemEntries(directory.Path),
                Is.EqualTo(new[] { databasePath }));
        }
    }

    /// <summary>Verifies creation never overwrites an existing output.</summary>
    [Test]
    public async Task CreateAsync_WithExistingOutput_PreservesIt()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("existing.mnote");
        await File.WriteAllTextAsync(databasePath, "existing");

        Assert.That(
            async () => await SchemaV2DatabaseFactory.CreateAsync(
                databasePath,
                "created-note",
                "Created Note"),
            Throws.TypeOf<IOException>());
        Assert.That(await File.ReadAllTextAsync(databasePath), Is.EqualTo("existing"));
    }

    /// <summary>Verifies pre-cancellation leaves no database, sidecar, or private output.</summary>
    [Test]
    public void CreateAsync_WithCancellation_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("cancelled.mnote");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await SchemaV2DatabaseFactory.CreateAsync(
                databasePath,
                "created-note",
                "Created Note",
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies lifecycle source provenance remains tied to the accepted handoff.</summary>
    [Test]
    public void LifecycleSources_RecordAcceptedSchemaLabHashes()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SchemaV2BaselineProvenance.ConnectionFactorySourceSha256,
                Is.EqualTo("0b2e33f199dc1fad5b48a55841a806d7f551fb3dea30ceee7c94b66537a9befe"));
            Assert.That(
                SchemaV2BaselineProvenance.DatabaseFilesSourceSha256,
                Is.EqualTo("b9ec57def9b8623ceb5410f3fa147d5640f1bcb03f17aee2254a51fd1655e73b"));
            Assert.That(
                SchemaV2BaselineProvenance.PendingOutputSourceSha256,
                Is.EqualTo("0eee62991898ef990cb922eff8fda78e4bdafe591ed911cc159854d66070fb08"));
            Assert.That(
                SchemaV2BaselineProvenance.DatabaseFactorySourceSha256,
                Is.EqualTo("30be0d906bdc583cf740c4f962f68d34123455f053291b6e90383e907a5a15c0"));
        }
    }

    private static async Task<int> ReadInt32Async(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyDictionary<string, string?>> ReadMetadataAsync(
        SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Metadata ORDER BY Key;";
        await using var reader = await command.ExecuteReaderAsync();
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            metadata.Add(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        return metadata;
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
