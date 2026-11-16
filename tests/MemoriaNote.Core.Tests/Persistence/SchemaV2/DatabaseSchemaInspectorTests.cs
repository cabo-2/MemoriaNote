using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies deterministic read-only Schema V2 observation.</summary>
[TestFixture]
[Category("Functional")]
public sealed class DatabaseSchemaInspectorTests
{
    /// <summary>Verifies release objects exclude FTS and retain SQLite-owned classification.</summary>
    [Test]
    public async Task InspectAsync_RecordsIdentityAndReleaseObjects()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("observed.mnote");
        await SchemaV2DatabaseFactory.CreateAsync(databasePath, "Observed");

        var observation = await DatabaseSchemaInspector.InspectAsync(databasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.ApplicationId, Is.EqualTo(SchemaV2Format.ApplicationId));
            Assert.That(observation.UserVersion, Is.EqualTo(SchemaV2Format.UserVersion));
            Assert.That(
                observation.SchemaObjects,
                Has.None.Matches<SchemaObjectObservation>(candidate =>
                    candidate is not null &&
                    (candidate.Name.Contains("Fts", StringComparison.OrdinalIgnoreCase) ||
                     candidate.Origin == "fts5-shadow")));
            Assert.That(
                observation.SchemaObjects.Single(candidate => candidate.Name == "sqlite_sequence").Origin,
                Is.EqualTo("sqlite-internal"));
            Assert.That(
                observation.SchemaObjects.Single(candidate =>
                    candidate.Name.StartsWith("sqlite_autoindex_Metadata", StringComparison.Ordinal)).Origin,
                Is.EqualTo("auto-index"));
        }
    }

    /// <summary>Verifies a missing path is rejected without creating a SQLite file.</summary>
    [Test]
    public void InspectAsync_MissingDatabase_ThrowsWithoutCreatingFile()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("missing.mnote");

        Assert.That(
            async () => await DatabaseSchemaInspector.InspectAsync(databasePath),
            Throws.TypeOf<FileNotFoundException>());
        Assert.That(Directory.GetFileSystemEntries(directory.Path), Is.Empty);
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
