using System.Text;
using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.IO;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies versioned performance manifests and their prepared fixture.</summary>
[TestFixture]
public sealed class ContractValidationTests
{
    /// <summary>Verifies the checked-in scenario manifest satisfies both validation layers.</summary>
    [Test]
    public async Task ScenarioManifest_CheckedInContract_IsValid()
    {
        var directory = System.IO.Path.Combine(
            TestPaths.RepositoryRoot,
            "performance",
            "scenarios");

        var manifests = new List<ScenarioManifestDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            manifests.Add(await ContractLoader.ReadScenarioManifestAsync(path));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifests, Has.Count.EqualTo(2));
            Assert.That(manifests, Has.All.Property("SchemaVersion").EqualTo("1"));
            Assert.That(
                manifests.SelectMany(manifest => manifest.Scenarios)
                    .Select(scenario => scenario.ScenarioId),
                Does.Contain("status-ready.publish-directory"));
            Assert.That(
                manifests.SelectMany(manifest => manifest.Scenarios),
                Has.All.Property("MeasurementCount").GreaterThanOrEqualTo(20));
        }
    }

    /// <summary>Verifies the checked-in synthetic dataset is byte-identical to its manifest.</summary>
    [Test]
    public async Task DatasetManifest_CheckedInFixture_HasExpectedDigestAndSize()
    {
        var manifestPath = System.IO.Path.Combine(
            TestPaths.RepositoryRoot,
            "performance",
            "datasets",
            "synthetic-small",
            "dataset-manifest.json");
        var manifest = await ContractLoader.ReadDatasetManifestAsync(manifestPath);
        var preparedPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(manifestPath)!,
            manifest.Prepared.RelativePath);

        var digest = await ContentHasher.HashDirectoryAsync(preparedPath);
        var totalBytes = Directory.EnumerateFiles(
                preparedPath,
                "*",
                SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(digest, Is.EqualTo(manifest.Prepared.Sha256));
            Assert.That(totalBytes, Is.EqualTo(manifest.Prepared.TotalBytes));
            Assert.That(manifest.Prepared.PageCount, Is.EqualTo(2));
        }
    }

    /// <summary>Verifies the locked dotnet/docs manifest satisfies the version 2 contract.</summary>
    [Test]
    public async Task DatasetManifest_DotnetDocsLock_IsValidVersionTwo()
    {
        var manifestPath = System.IO.Path.Combine(
            TestPaths.RepositoryRoot,
            "performance",
            "datasets",
            "dotnet-docs-en",
            "dataset-manifest.json");

        var manifest = await ContractLoader.ReadDatasetManifestAsync(manifestPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.SchemaVersion, Is.EqualTo("2"));
            Assert.That(manifest.DatasetId, Is.EqualTo("dotnet-docs-en"));
            Assert.That(manifest.Source.Kind, Is.EqualTo("git"));
            Assert.That(manifest.Source.ContentSha256, Has.Length.EqualTo(64));
            Assert.That(manifest.Prepared.PageCount, Is.EqualTo(10000));
            Assert.That(manifest.Statistics, Is.Not.Null);
            Assert.That(manifest.Statistics!.EligiblePageCount, Is.GreaterThan(10000));
        }
    }

    /// <summary>Verifies JSON Schema rejects fields not included in the contract.</summary>
    [Test]
    public void ScenarioManifest_WithUnknownProperty_IsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "scenario.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": "1",
              "unexpected": true,
              "scenarios": []
            }
            """,
            Encoding.UTF8);

        var exception = Assert.ThrowsAsync<ContractException>(
            async () => await ContractLoader.ReadScenarioManifestAsync(path));

        Assert.That(exception!.Message, Does.Contain("additional property"));
    }

    /// <summary>Verifies duplicate JSON properties cannot override an earlier value.</summary>
    [Test]
    public void ScenarioManifest_WithDuplicateProperty_IsRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "scenario.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": "1",
              "schemaVersion": "1",
              "scenarios": []
            }
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var exception = Assert.ThrowsAsync<ContractException>(
            async () => await ContractLoader.ReadScenarioManifestAsync(path));

        Assert.That(exception!.Message, Does.Contain("duplicate property"));
    }

    /// <summary>Verifies semantic validation catches duplicate scenario identities.</summary>
    [Test]
    public void ScenarioManifest_WithDuplicateIdentity_IsRejected()
    {
        var scenario = ValidScenario("duplicate");
        var manifest = new ScenarioManifestDocument
        {
            SchemaVersion = "1",
            Scenarios = [scenario, ValidScenario("duplicate")]
        };

        var issues = ManifestValidators.Validate(manifest);

        Assert.That(issues, Has.Some.Contains("duplicated"));
    }

    static ScenarioDefinition ValidScenario(string identifier)
    {
        return new ScenarioDefinition
        {
            ScenarioId = identifier,
            ScenarioVersion = "1",
            Description = "test scenario",
            Availability = ["test"],
            ArtifactInputKind = "publish-directory",
            CacheMode = "process-cold",
            Command = new ProcessCommandDefinition(),
            Expected = new ExpectedProcessResult(),
            MeasurementCount = 20,
            TimeoutMilliseconds = 1000,
            ResetPolicy = "per-trial"
        };
    }
}
