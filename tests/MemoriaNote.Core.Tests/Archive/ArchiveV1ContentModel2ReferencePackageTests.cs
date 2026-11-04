using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Archive;

/// <summary>Verifies the amended CM2 draft reference package as a specification artifact.</summary>
[TestFixture]
[Category("Functional")]
public sealed class ArchiveV1ContentModel2ReferencePackageTests
{
    private static readonly string PackageDirectory = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "reference",
        "archive",
        "v1-content-model-v2");

    private static readonly string[] ManagedKeys =
    {
        "Author",
        "CreateTime",
        "Description",
        "ReadOnly",
        "Tag",
        "Title"
    };

    /// <summary>Verifies the JSON Schema expresses the six managed keys and retired-key rejection.</summary>
    [Test]
    public void MetadataSchema_DescribesAmendedDraftContract()
    {
        using var schema = JsonDocument.Parse(ReadPackageBytes("schema", "metadata.schema.json"));
        var root = schema.RootElement;
        var keySchema = root.GetProperty("items")
            .GetProperty("properties")
            .GetProperty("key");
        var retired = keySchema.GetProperty("not")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        var contracts = root.GetProperty("allOf")
            .EnumerateArray()
            .Select(item => new
            {
                Key = item.GetProperty("contains")
                    .GetProperty("properties")
                    .GetProperty("key")
                    .GetProperty("const")
                    .GetString(),
                Value = item.GetProperty("contains")
                    .GetProperty("properties")
                    .GetProperty("value"),
                Minimum = item.GetProperty("minContains").GetInt32(),
                Maximum = item.GetProperty("maxContains").GetInt32()
            })
            .ToArray();
        var byKey = contracts.ToDictionary(contract => contract.Key!, StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("$schema").GetString(),
                Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
            Assert.That(root.GetProperty("$id").GetString(),
                Is.EqualTo("urn:memoria-note:archive:v1:content-model:v2:metadata"));
            Assert.That(retired, Is.EqualTo(new[] { "Name", "Version" }));
            Assert.That(contracts.Select(contract => contract.Key), Is.EqualTo(ManagedKeys));
            Assert.That(contracts, Has.All.Property("Minimum").EqualTo(1));
            Assert.That(contracts, Has.All.Property("Maximum").EqualTo(1));
            Assert.That(
                byKey["CreateTime"].Value.GetProperty("type")
                    .EnumerateArray()
                    .Select(value => value.GetString()),
                Is.EqualTo(new[] { "string", "null" }));
            Assert.That(
                byKey["CreateTime"].Value.GetProperty("pattern").GetString(),
                Is.EqualTo("^[0-9]{4}(0[1-9]|1[0-2])([0-2][0-9]|3[01])" +
                    "(0[1-9]|1[0-2])[0-5][0-9][0-5][0-9]$"));
            Assert.That(
                byKey["ReadOnly"].Value.GetProperty("enum")
                    .EnumerateArray()
                    .Select(value => value.GetString()),
                Is.EqualTo(new[] { "False", "True" }));
            Assert.That(
                ManagedKeys.Except(new[] { "CreateTime", "ReadOnly" }, StringComparer.Ordinal)
                    .Select(key => byKey[key].Value.GetProperty("type").GetString()),
                Has.All.EqualTo("string"));
        }
    }

    /// <summary>Verifies the manifest Schema keeps the identity-derived format tuple.</summary>
    [Test]
    public void ManifestSchema_DescribesIdentityDerivedSourceVersion()
    {
        using var schema = JsonDocument.Parse(ReadPackageBytes("schema", "manifest.schema.json"));
        var sourceVersion = schema.RootElement
            .GetProperty("properties")
            .GetProperty("sourceNotebookFormatVersion");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourceVersion.GetProperty("const").GetString(), Is.EqualTo("2"));
            Assert.That(sourceVersion.GetProperty("description").GetString(),
                Does.Contain("schema identity").And.Not.Contain("Metadata.Version"));
        }
    }

    /// <summary>Verifies valid metadata examples satisfy the amended managed and extension contract.</summary>
    [TestCase("valid")]
    [TestCase("compatibility", "empty")]
    [TestCase("compatibility", "single-page")]
    [TestCase("compatibility", "full-field")]
    public void ValidMetadataExample_MatchesAmendedDraft(params string[] segments)
    {
        var rows = ReadMetadata(segments.Append("metadata.json").ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ValidateMetadata(rows), Is.Empty);
            Assert.That(
                rows.Select(row => row.Key),
                Is.Ordered.Using<string>(StringComparer.Ordinal));
        }
    }

    /// <summary>Verifies the full-field fixture covers unknown null, empty, and text values.</summary>
    [Test]
    public void FullFieldFixture_CoversLosslessUnknownMetadataStates()
    {
        var rows = ReadMetadata("compatibility", "full-field", "metadata.json");
        var unknown = rows
            .Where(row => !ManagedKeys.Contains(row.Key, StringComparer.Ordinal))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown.Select(row => row.Value), Has.Some.Null);
            Assert.That(unknown.Select(row => row.Value), Has.Some.EqualTo(string.Empty));
            Assert.That(unknown.Select(row => row.Value), Has.Some.Matches<string>(value =>
                !string.IsNullOrEmpty(value)));
        }
    }

    /// <summary>Verifies each invalid metadata example isolates an amended contract violation.</summary>
    [TestCase("metadata-duplicate-key.json", "duplicate:Title")]
    [TestCase("metadata-invalid-create-time.json", "invalid:CreateTime")]
    [TestCase("metadata-invalid-read-only.json", "invalid:ReadOnly")]
    [TestCase("metadata-missing-managed-key.json", "missing:Author")]
    [TestCase("metadata-null-managed-value.json", "null:Title")]
    [TestCase("metadata-retired-name.json", "retired:Name")]
    [TestCase("metadata-retired-version.json", "retired:Version")]
    public void InvalidMetadataExample_IsolatesAmendedRule(
        string fileName,
        string expectedIssue)
    {
        var rows = ReadMetadata("invalid", fileName);

        Assert.That(ValidateMetadata(rows), Is.EqualTo(new[] { expectedIssue }));
    }

    /// <summary>Verifies every complete example manifest describes the exact repository payload bytes.</summary>
    [TestCase("valid")]
    [TestCase("compatibility", "empty")]
    [TestCase("compatibility", "single-page")]
    [TestCase("compatibility", "full-field")]
    public void Manifest_DescribesExactPayloadBytes(params string[] segments)
    {
        var manifestBytes = ReadExampleBytes(segments.Append("manifest.json").ToArray());
        var metadata = ReadExampleBytes(segments.Append("metadata.json").ToArray());
        var pages = ReadExampleBytes(segments.Append("pages.ndjson").ToArray());
        using var manifest = JsonDocument.Parse(manifestBytes);
        using var metadataDocument = JsonDocument.Parse(metadata);
        var root = manifest.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("archiveFormatVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("contentModelVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("sourceNotebookFormatVersion").GetString(), Is.EqualTo("2"));
            AssertDescriptor(
                root.GetProperty("dataSets").GetProperty("metadata"),
                metadata,
                metadataDocument.RootElement.GetArrayLength());
            AssertDescriptor(
                root.GetProperty("dataSets").GetProperty("pages"),
                pages,
                pages.Count(value => value == (byte)'\n'));
        }
    }

    private static IReadOnlyList<string> ValidateMetadata(IReadOnlyList<MetadataRow> rows)
    {
        var issues = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!keys.Add(row.Key))
                issues.Add($"duplicate:{row.Key}");
            if (row.Key is "Name" or "Version")
                issues.Add($"retired:{row.Key}");
            if (ManagedKeys.Contains(row.Key, StringComparer.Ordinal) &&
                row.Key != "CreateTime" && row.Value is null)
            {
                issues.Add($"null:{row.Key}");
            }
            if (row.Key == "ReadOnly" && row.Value is not ("False" or "True"))
                issues.Add("invalid:ReadOnly");
            if (row.Key == "CreateTime" && row.Value is not null &&
                !DateTime.TryParseExact(
                    row.Value,
                    "yyyyMMddhhmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                issues.Add("invalid:CreateTime");
            }
        }

        foreach (var key in ManagedKeys)
        {
            if (!keys.Contains(key))
                issues.Add($"missing:{key}");
        }
        return issues;
    }

    private static IReadOnlyList<MetadataRow> ReadMetadata(params string[] segments)
    {
        using var document = JsonDocument.Parse(ReadExampleBytes(segments));
        return document.RootElement.EnumerateArray()
            .Select(element => new MetadataRow(
                element.GetProperty("key").GetString()!,
                element.GetProperty("value").ValueKind == JsonValueKind.Null
                    ? null
                    : element.GetProperty("value").GetString()))
            .ToArray();
    }

    private static void AssertDescriptor(JsonElement descriptor, byte[] payload, int count)
    {
        Assert.That(descriptor.GetProperty("recordCount").GetInt32(), Is.EqualTo(count));
        Assert.That(descriptor.GetProperty("uncompressedByteLength").GetInt64(),
            Is.EqualTo(payload.LongLength));
        Assert.That(descriptor.GetProperty("sha256").GetString(),
            Is.EqualTo(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()));
    }

    private static byte[] ReadPackageBytes(params string[] segments) =>
        File.ReadAllBytes(Path.Combine(new[] { PackageDirectory }.Concat(segments).ToArray()));

    private static byte[] ReadExampleBytes(params string[] segments) =>
        ReadPackageBytes(new[] { "examples" }.Concat(segments).ToArray());

    private sealed record MetadataRow(string Key, string? Value);
}
