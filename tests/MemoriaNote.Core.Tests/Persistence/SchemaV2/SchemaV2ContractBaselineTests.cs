using System.Security.Cryptography;
using System.Text.Json;
using MemoriaNote.Persistence.SchemaV2;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the fixed SchemaLab baseline accepted for Schema V2.</summary>
[TestFixture]
[Category("Functional")]
public sealed class SchemaV2ContractBaselineTests
{
    /// <summary>Verifies embedded assets retain their accepted SchemaLab hashes.</summary>
    [Test]
    public async Task EmbeddedAssets_MatchAcceptedSchemaLabBaseline()
    {
        await using var ddl = SchemaV2Resources.OpenCanonicalDdl();
        await using var contract = SchemaV2Resources.OpenExpectedContract();

        var ddlHash = await ComputeSha256Async(ddl);
        var contractHash = await ComputeSha256Async(contract);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SchemaV2BaselineProvenance.HandoffManifestVersion,
                Is.EqualTo(1));
            Assert.That(
                SchemaV2BaselineProvenance.SchemaLabFreezeCommit,
                Is.EqualTo("5da289582ccf475dfe4c1d7e6dcbf3bb6b4ec4a7"));
            Assert.That(
                ddlHash,
                Is.EqualTo(SchemaV2BaselineProvenance.CanonicalDdlSha256));
            Assert.That(
                contractHash,
                Is.EqualTo(SchemaV2BaselineProvenance.ExpectedContractSha256));
        }
    }

    /// <summary>Verifies format constants agree with the machine-readable contract.</summary>
    [Test]
    public void FormatIdentity_MatchesExpectedContract()
    {
        using var contractStream = SchemaV2Resources.OpenExpectedContract();
        using var contract = JsonDocument.Parse(contractStream);
        var root = contract.RootElement;
        var authoritativeTables = ReadStringArray(
            root.GetProperty("authoritativeTables"));
        var derivedObjects = ReadStringArray(root.GetProperty("derivedObjects"));
        var indexedColumns = ReadStringArray(
            root.GetProperty("fts5").GetProperty("indexedColumns"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SchemaV2Format.SchemaId, Is.EqualTo("schema-v2"));
            Assert.That(
                root.GetProperty("applicationId").GetInt32(),
                Is.EqualTo(SchemaV2Format.ApplicationId));
            Assert.That(
                root.GetProperty("userVersion").GetInt32(),
                Is.EqualTo(SchemaV2Format.UserVersion));
            Assert.That(
                root.GetProperty("persistedNotebookFormatVersion").GetString(),
                Is.EqualTo(SchemaV2Format.MetadataVersion));
            Assert.That(
                root.GetProperty("allowsUnknownMetadataKeys").GetBoolean(),
                Is.True);
            Assert.That(
                root.GetProperty("fts5").GetProperty("tokenizer").GetString(),
                Is.EqualTo("unicode61"));
            Assert.That(
                authoritativeTables,
                Is.EqualTo(new[] { "Metadata", "Pages" }));
            Assert.That(
                derivedObjects,
                Is.EqualTo(new[] { "PageTags", "FtsIndex" }));
            Assert.That(
                indexedColumns,
                Is.EqualTo(new[] { "Name", "Text" }));
        }
    }

    /// <summary>Verifies canonical DDL creates exactly the expected schema objects.</summary>
    [Test]
    public async Task CanonicalDdl_CreatesExpectedSchemaObjects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SchemaV2Resources.ReadCanonicalDdl();
            await command.ExecuteNonQueryAsync();
        }

        var applicationId = await ReadPragmaAsync(connection, "application_id");
        var userVersion = await ReadPragmaAsync(connection, "user_version");
        var actualObjects = await ReadSchemaObjectsAsync(connection);
        var expectedObjects = ReadExpectedSchemaObjects();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(applicationId, Is.EqualTo(SchemaV2Format.ApplicationId));
            Assert.That(userVersion, Is.EqualTo(SchemaV2Format.UserVersion));
            Assert.That(actualObjects, Is.EqualTo(expectedObjects));
        }
    }

    private static async Task<string> ComputeSha256Async(Stream stream)
    {
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<int> ReadPragmaAsync(
        SqliteConnection connection,
        string pragmaName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<string>> ReadSchemaObjectsAsync(
        SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, tbl_name
            FROM sqlite_schema
            ORDER BY type, name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var objects = new List<string>();
        while (await reader.ReadAsync())
        {
            objects.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
        }

        return objects
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadExpectedSchemaObjects()
    {
        using var contractStream = SchemaV2Resources.OpenExpectedContract();
        using var contract = JsonDocument.Parse(contractStream);
        return contract.RootElement
            .GetProperty("schemaObjects")
            .EnumerateArray()
            .Select(item =>
                $"{item.GetProperty("type").GetString()}|" +
                $"{item.GetProperty("name").GetString()}|" +
                item.GetProperty("tableName").GetString())
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array)
    {
        return array
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
    }
}
