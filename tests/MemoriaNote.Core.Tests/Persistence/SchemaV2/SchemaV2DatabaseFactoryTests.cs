using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
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

    /// <summary>Verifies a failed publication gate removes its private database.</summary>
    [Test]
    public void CreateAsync_WithValidationFailure_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("invalid.mnote");
        var invalidLayer = SchemaV2ValidationLayerResult.Evaluated(
            new[] { "injected validation failure" });
        var validLayer = SchemaV2ValidationLayerResult.Evaluated(Array.Empty<string>());
        var invalid = new SchemaV2DatabaseValidation(
            validLayer,
            invalidLayer,
            validLayer,
            validLayer,
            SchemaV2DerivedDataValidation.NotEvaluated);

        Assert.That(
            async () => await SchemaV2DatabaseFactory.CreateAsync(
                databasePath,
                "created-note",
                "Created Note",
                (_, _) => Task.FromResult(invalid)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("injected validation failure"));
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
    }

    /// <summary>Verifies cancellation in the publication gate removes private output.</summary>
    [Test]
    public void CreateAsync_WithValidationCancellation_LeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("validation-cancelled.mnote");
        using var cancellation = new CancellationTokenSource();

        Assert.That(
            async () => await SchemaV2DatabaseFactory.CreateAsync(
                databasePath,
                "created-note",
                "Created Note",
                (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Unreachable.");
                },
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
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
