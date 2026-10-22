using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.Runner;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance.Tests;

/// <summary>Verifies a complete bounded runner workflow against a helper executable.</summary>
[TestFixture]
public sealed class PerformanceRunnerTests
{
    /// <summary>Verifies a successful run retains raw samples and a valid result file.</summary>
    [Test]
    public async Task RunAsync_ValidScenario_WritesRevalidatableResult()
    {
        using var temporary = new TemporaryDirectory();
        var manifestPath = await WriteManifestAsync(temporary.Path, "ok");
        var outputPath = System.IO.Path.Combine(temporary.Path, "result.json");

        var result = await new PerformanceRunner().RunAsync(
            Options(temporary.Path, manifestPath, outputPath));
        var reloaded = await ContractLoader.ReadResultAsync(outputPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Run.Status, Is.EqualTo("valid"));
            Assert.That(result.ScenarioResults.Single().Samples, Has.Count.EqualTo(20));
            Assert.That(result.ScenarioResults.Single().Summary, Is.Not.Null);
            Assert.That(ResultValidator.Validate(reloaded), Is.Empty);
            Assert.That(Directory.Exists(
                System.IO.Path.Combine(temporary.Path, $"MemoriaNote.Performance-{result.Run.RunId}")),
                Is.False);
        }
    }

    /// <summary>Verifies output mismatch invalidates the run without discarding the sample.</summary>
    [Test]
    public async Task RunAsync_OutputMismatch_WritesInvalidResultWithoutSummary()
    {
        using var temporary = new TemporaryDirectory();
        var manifestPath = await WriteManifestAsync(temporary.Path, "missing text");
        var outputPath = System.IO.Path.Combine(temporary.Path, "invalid-result.json");

        var result = await new PerformanceRunner().RunAsync(
            Options(temporary.Path, manifestPath, outputPath));
        var scenario = result.ScenarioResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Run.Status, Is.EqualTo("invalid"));
            Assert.That(scenario.Samples, Has.Count.EqualTo(1));
            Assert.That(scenario.Summary, Is.Null);
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(File.Exists(outputPath), Is.True);
        }
    }

    /// <summary>Verifies post-process file state is checked outside the measured child process.</summary>
    [Test]
    public async Task RunAsync_StatefulScenario_ValidatesExpectedFile()
    {
        using var temporary = new TemporaryDirectory();
        var manifestPath = System.IO.Path.Combine(temporary.Path, "stateful.json");
        var outputPath = System.IO.Path.Combine(temporary.Path, "stateful-result.json");
        var manifest = CreateManifest(string.Empty);
        manifest.Scenarios[0].Command.Arguments =
            ["write-file", "{workspace}/created.txt", "state"];
        manifest.Scenarios[0].Expected.StandardOutputContains = [];
        manifest.Scenarios[0].Expected.StandardOutputEmpty = true;
        manifest.Scenarios[0].Expected.FileSystemEntries =
        [
            new ExpectedFileSystemEntry
            {
                Path = "{workspace}/created.txt",
                Kind = "file",
                Exists = true,
                SizeBytes = 5
            }
        ];
        await ContractJson.WriteAsync(manifestPath, manifest);

        var result = await new PerformanceRunner().RunAsync(
            Options(temporary.Path, manifestPath, outputPath));

        Assert.That(result.Run.Status, Is.EqualTo("valid"));
    }

    /// <summary>Verifies a prepared dataset mismatch invalidates the result before measurement.</summary>
    [Test]
    public async Task RunAsync_DatasetChecksumMismatch_WritesPrerequisiteFailure()
    {
        using var temporary = new TemporaryDirectory();
        var preparedDirectory = System.IO.Path.Combine(temporary.Path, "prepared");
        Directory.CreateDirectory(preparedDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(preparedDirectory, "pages.ndjson"),
            "x");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temporary.Path, "LICENSE.md"),
            "test license");
        var datasetPath = System.IO.Path.Combine(temporary.Path, "dataset.json");
        await ContractJson.WriteAsync(
            datasetPath,
            new DatasetManifestDocument
            {
                SchemaVersion = "1",
                DatasetId = "test-dataset",
                DatasetVersion = "1",
                Languages = ["en"],
                Source = new DatasetSource
                {
                    Name = "test",
                    Url = "https://example.invalid/dataset",
                    Revision = "1"
                },
                Licenses =
                [
                    new DatasetLicense
                    {
                        Expression = "MIT",
                        AppliesTo = "prepared/**/*",
                        LicenseFile = "LICENSE.md",
                        Attribution = "test"
                    }
                ],
                Selection = new DatasetSelection
                {
                    Description = "test scale",
                    Scales = [new DatasetScale { ScaleId = "tiny", PageCount = 1 }]
                },
                Transform = new DatasetTransform { Name = "test", Version = "1" },
                Prepared = new PreparedDataset
                {
                    RelativePath = "prepared",
                    Sha256 = new string('a', 64),
                    PageCount = 1,
                    TotalBytes = 1
                }
            });
        var scenarioPath = System.IO.Path.Combine(temporary.Path, "dataset-scenario.json");
        var manifest = CreateManifest("ok");
        manifest.Scenarios[0].DatasetId = "test-dataset";
        manifest.Scenarios[0].ScaleId = "tiny";
        await ContractJson.WriteAsync(scenarioPath, manifest);
        var outputPath = System.IO.Path.Combine(temporary.Path, "dataset-result.json");
        var options = Options(temporary.Path, scenarioPath, outputPath);
        options = options with { DatasetManifestPath = datasetPath };

        var result = await new PerformanceRunner().RunAsync(options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Run.Status, Is.EqualTo("invalid"));
            Assert.That(result.ScenarioResults.Single().Samples, Is.Empty);
            Assert.That(result.Failures.Single().Code, Is.EqualTo("prerequisite"));
            Assert.That(result.Failures.Single().Message, Does.Contain("checksum mismatch"));
        }
    }

    static RunOptions Options(
        string temporaryRoot,
        string manifestPath,
        string outputPath)
    {
        return new RunOptions
        {
            ScenarioManifestPath = manifestPath,
            ExecutablePath = TestPaths.TestTargetExecutable,
            ArtifactId = "test-target",
            ProductVersion = "1.0.0",
            SourceCommit = "test-commit",
            Rid = "test-x64",
            DeploymentKind = "self-contained",
            ArtifactInputKind = "publish-directory",
            EnvironmentLabel = "test-environment",
            OutputPath = outputPath,
            TemporaryRoot = temporaryRoot
        };
    }

    static async Task<string> WriteManifestAsync(string directory, string expectedText)
    {
        var path = System.IO.Path.Combine(directory, "scenario.json");
        var manifest = CreateManifest(expectedText);
        await ContractJson.WriteAsync(path, manifest);
        return path;
    }

    static ScenarioManifestDocument CreateManifest(string expectedText)
    {
        return new ScenarioManifestDocument
        {
            SchemaVersion = "1",
            Scenarios =
            [
                new ScenarioDefinition
                {
                    ScenarioId = "helper-success",
                    ScenarioVersion = "1",
                    Description = "Runs the bounded helper process.",
                    Availability = ["test"],
                    ArtifactInputKind = "publish-directory",
                    CacheMode = "process-cold",
                    Command = new ProcessCommandDefinition
                    {
                        Arguments = ["success", "ok"]
                    },
                    Expected = new ExpectedProcessResult
                    {
                        ExitCode = 0,
                        StandardOutputContains = [expectedText],
                        StandardErrorEmpty = true
                    },
                    WarmupCount = 0,
                    MeasurementCount = 20,
                    TimeoutMilliseconds = 10000,
                    ResetPolicy = "per-trial"
                }
            ]
        };
    }
}
