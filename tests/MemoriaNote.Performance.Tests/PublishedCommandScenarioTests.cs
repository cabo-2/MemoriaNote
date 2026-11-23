using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.IO;
using MemoriaNote.Performance.Runner;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance.Tests;

/// <summary>Runs the new published-command scenarios against a bounded fixture.</summary>
[TestFixture]
public sealed class PublishedCommandScenarioTests
{
    /// <summary>Verifies schema v2 ls glob and find workflows remain executable in CI.</summary>
    [Test]
    public async Task SyntheticSchemaV2Scenarios_CompleteWithBoundedSamples()
    {
        using var temporary = new TemporaryDirectory();
        var repositoryRoot = TestPaths.RepositoryRoot;
        var sourceManifestPath = Path.Combine(
            repositoryRoot,
            "performance",
            "scenarios",
            "scenario-manifest-v1.synthetic-small.extracted-archive.json");
        var manifest = await ContractLoader.ReadScenarioManifestAsync(sourceManifestPath);
        foreach (var scenario in manifest.Scenarios)
            scenario.ArtifactInputKind = "publish-directory";
        var manifestPath = Path.Combine(temporary.Path, "scenarios.json");
        await ContractJson.WriteAsync(manifestPath, manifest);

        var (datasetManifestPath, datasetRoot) = await CreateDatasetAsync(
            temporary.Path);
        var outputPath = Path.Combine(temporary.Path, "result.json");
        var result = await new PerformanceRunner().RunAsync(
            new RunOptions
            {
                ScenarioManifestPath = manifestPath,
                DatasetManifestPath = datasetManifestPath,
                DatasetRoot = datasetRoot,
                ExecutablePath = TestPaths.CliExecutable,
                ArtifactId = "ci-current-build",
                ProductVersion = "development",
                SourceCommit = "current-checkout",
                Rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
                DeploymentKind = "self-contained",
                ArtifactInputKind = "publish-directory",
                ScenarioSupportLevel = "phase-11",
                EnvironmentLabel = "ci-bounded-synthetic",
                OutputPath = outputPath,
                TemporaryRoot = temporary.Path
            });

        using (Assert.EnterMultipleScope())
        {
            var failureText = string.Join(
                Environment.NewLine,
                result.Failures.Select(failure =>
                    $"{failure.Code}: {failure.Message}"));
            Assert.That(result.Failures, Is.Empty, failureText);
            Assert.That(result.Run.Status, Is.EqualTo("valid"), failureText);
            Assert.That(result.ScenarioResults, Has.Count.EqualTo(2));
            Assert.That(
                result.ScenarioResults.Select(scenario => scenario.Status),
                Is.All.EqualTo("valid"));
            Assert.That(
                result.ScenarioResults.Select(scenario => scenario.Samples.Count),
                Is.All.EqualTo(20));
            Assert.That(ResultValidator.Validate(result), Is.Empty);
        }
    }

    static async Task<(string ManifestPath, string PreparedPath)> CreateDatasetAsync(
        string root)
    {
        var preparedPath = Path.Combine(root, "prepared");
        Directory.CreateDirectory(preparedPath);
        await File.WriteAllTextAsync(
            Path.Combine(preparedPath, "english-note.txt"),
            "A small deterministic note for harness contract tests.\n");
        await File.WriteAllTextAsync(
            Path.Combine(preparedPath, "日本語ノート.txt"),
            "空白なしの日本語本文とemoji 🚀を含む、小さな決定的fixtureです。\n");
        await File.WriteAllTextAsync(Path.Combine(root, "LICENSE.md"), "MIT\n");
        var totalBytes = Directory.EnumerateFiles(preparedPath)
            .Sum(path => new FileInfo(path).Length);
        var manifestPath = Path.Combine(root, "dataset.json");
        await ContractJson.WriteAsync(
            manifestPath,
            new DatasetManifestDocument
            {
                SchemaVersion = "1",
                DatasetId = "synthetic-small",
                DatasetVersion = "ci-1",
                Languages = ["en", "ja"],
                Source = new DatasetSource
                {
                    Name = "Bounded CI fixture",
                    Url = "https://example.invalid/ci-fixture",
                    Revision = "ci-1"
                },
                Licenses =
                [
                    new DatasetLicense
                    {
                        Expression = "MIT",
                        AppliesTo = "prepared/**/*",
                        LicenseFile = "LICENSE.md",
                        Attribution = "Memoria Note contributors"
                    }
                ],
                Selection = new DatasetSelection
                {
                    Description = "Two deterministic pages.",
                    Scales =
                    [
                        new DatasetScale
                        {
                            ScaleId = "tiny-2",
                            PageCount = 2
                        }
                    ]
                },
                Transform = new DatasetTransform
                {
                    Name = "hand-authored-text",
                    Version = "1"
                },
                Prepared = new PreparedDataset
                {
                    RelativePath = "prepared",
                    Sha256 = await ContentHasher.HashDirectoryAsync(preparedPath),
                    PageCount = 2,
                    TotalBytes = totalBytes
                }
            });
        return (manifestPath, preparedPath);
    }
}
