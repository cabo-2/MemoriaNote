using System.Text;
using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.Execution;
using MemoriaNote.Performance.IO;
using MemoriaNote.Performance.Statistics;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance.Runner;

internal sealed class PerformanceRunner
{
    readonly ProcessExecutor _processExecutor;

    internal PerformanceRunner(ProcessExecutor? processExecutor = null)
    {
        _processExecutor = processExecutor ?? new ProcessExecutor();
    }

    internal async Task<PerformanceResultDocument> RunAsync(
        RunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var manifest = await ContractLoader.ReadScenarioManifestAsync(
            options.ScenarioManifestPath,
            cancellationToken);
        var scenarios = SelectScenarios(manifest, options.ScenarioIds);
        ValidateOptions(options, scenarios);

        var executablePath = Path.GetFullPath(options.ExecutablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("The published executable was not found.", executablePath);
        var runId = Guid.NewGuid().ToString("N");
        var parent = options.TemporaryRoot == null
            ? Path.GetTempPath()
            : Path.GetFullPath(options.TemporaryRoot);
        Directory.CreateDirectory(parent);
        var runRoot = Path.Combine(parent, $"MemoriaNote.Performance-{runId}");
        Directory.CreateDirectory(runRoot);

        try
        {
            var result = await CreateResultAsync(
                options,
                executablePath,
                runId,
                runRoot,
                cancellationToken);
            DatasetManifestDocument? datasetManifest = null;
            string? datasetDirectory = null;
            string? datasetIssue = null;
            if (options.DatasetManifestPath != null)
            {
                datasetManifest = await ContractLoader.ReadDatasetManifestAsync(
                    options.DatasetManifestPath,
                    cancellationToken);
                datasetDirectory = ResolveDatasetDirectory(options, datasetManifest);
                var actualHash = await ContentHasher.HashDirectoryAsync(
                    datasetDirectory,
                    cancellationToken);
                if (!string.Equals(
                    actualHash,
                    datasetManifest.Prepared.Sha256,
                    StringComparison.Ordinal))
                {
                    datasetIssue =
                        $"Prepared dataset checksum mismatch: expected " +
                        $"{datasetManifest.Prepared.Sha256}, actual {actualHash}.";
                }
                var actualBytes = Directory.EnumerateFiles(
                        datasetDirectory,
                        "*",
                        SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length);
                if (actualBytes != datasetManifest.Prepared.TotalBytes)
                {
                    datasetIssue = AppendIssue(
                        datasetIssue,
                        $"Prepared dataset byte count mismatch: expected " +
                        $"{datasetManifest.Prepared.TotalBytes}, actual {actualBytes}.");
                }
                var manifestDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(options.DatasetManifestPath))!;
                foreach (var license in datasetManifest.Licenses)
                {
                    var licensePath = Path.GetFullPath(
                        Path.Combine(manifestDirectory, license.LicenseFile));
                    if (!File.Exists(licensePath))
                    {
                        datasetIssue = AppendIssue(
                            datasetIssue,
                            $"Dataset license file '{license.LicenseFile}' was not found.");
                    }
                }
                result.Dataset = new DatasetIdentity
                {
                    DatasetId = datasetManifest.DatasetId,
                    DatasetVersion = datasetManifest.DatasetVersion,
                    SourceRevision = datasetManifest.Source.Revision,
                    PreparedSha256 = datasetManifest.Prepared.Sha256
                };
            }

            foreach (var scenario in scenarios)
            {
                var prerequisiteIssue = ValidateScenarioPrerequisites(
                    scenario,
                    datasetManifest,
                    datasetIssue);
                ScenarioExecution execution;
                if (prerequisiteIssue == null)
                {
                    execution = await ExecuteScenarioAsync(
                        scenario,
                        executablePath,
                        runRoot,
                        datasetDirectory,
                        options.KeepTemporary,
                        cancellationToken);
                }
                else
                {
                    execution = ScenarioExecution.Invalid(
                        scenario,
                        "prerequisite",
                        prerequisiteIssue);
                }

                result.ScenarioResults.Add(execution.Result);
                result.Failures.AddRange(execution.Failures);
            }

            result.Run.Status = result.ScenarioResults.All(
                scenario => scenario.Status == "valid") ? "valid" : "invalid";
            var validationIssues = ResultValidator.Validate(result);
            if (validationIssues.Count > 0)
            {
                throw new ContractException(
                    "The runner generated an invalid result:" + Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        validationIssues.Select(issue => $"- {issue}")));
            }
            await ContractJson.WriteAsync(options.OutputPath, result, cancellationToken);
            return result;
        }
        finally
        {
            if (!options.KeepTemporary && Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
    }

    async Task<PerformanceResultDocument> CreateResultAsync(
        RunOptions options,
        string executablePath,
        string runId,
        string runRoot,
        CancellationToken cancellationToken)
    {
        var runner = RunnerIdentity.Read();
        return new PerformanceResultDocument
        {
            Run = new RunIdentity
            {
                RunId = runId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                RunnerVersion = runner.Version,
                RunnerCommit = runner.Commit,
                Status = "invalid"
            },
            Target = new TargetIdentity
            {
                ArtifactId = options.ArtifactId,
                ProductVersion = options.ProductVersion,
                SourceCommit = options.SourceCommit,
                Rid = options.Rid,
                Configuration = options.Configuration,
                DeploymentKind = options.DeploymentKind,
                ArtifactInputKind = options.ArtifactInputKind,
                ExecutableSha256 = await ContentHasher.HashFileAsync(
                    executablePath,
                    cancellationToken),
                ExecutableSizeBytes = new FileInfo(executablePath).Length,
                ArchiveSha256 = options.ArchivePath == null
                    ? null
                    : await ContentHasher.HashFileAsync(options.ArchivePath, cancellationToken)
            },
            Environment = EnvironmentProbe.Capture(
                options.EnvironmentLabel,
                runRoot,
                options.PowerModeNote)
        };
    }

    async Task<ScenarioExecution> ExecuteScenarioAsync(
        ScenarioDefinition scenario,
        string executablePath,
        string runRoot,
        string? datasetDirectory,
        bool keepTemporary,
        CancellationToken cancellationToken)
    {
        var result = CreateScenarioResult(scenario);
        var failures = new List<RunFailure>();
        TrialContext? reusedContext = null;
        try
        {
            if (scenario.ResetPolicy == "reuse-state")
            {
                reusedContext = new TrialContext(
                    runRoot,
                    $"{scenario.ScenarioId}-shared",
                    datasetDirectory,
                    keepTemporary);
                var arrangeIssue = await ArrangeAsync(
                    scenario,
                    executablePath,
                    reusedContext,
                    cancellationToken);
                if (arrangeIssue != null)
                {
                    return ScenarioExecution.Invalid(
                        scenario,
                        "arrange-failure",
                        arrangeIssue);
                }
            }

            for (var warmup = 0; warmup < scenario.WarmupCount; warmup++)
            {
                using var trial = scenario.ResetPolicy == "per-trial"
                    ? new TrialContext(
                        runRoot,
                        $"{scenario.ScenarioId}-warmup-{warmup}",
                        datasetDirectory,
                        keepTemporary)
                    : null;
                var context = trial ?? reusedContext!;
                if (trial != null)
                {
                    var arrangeIssue = await ArrangeAsync(
                        scenario,
                        executablePath,
                        context,
                        cancellationToken);
                    if (arrangeIssue != null)
                    {
                        return ScenarioExecution.Invalid(
                            scenario,
                            "warmup-arrange-failure",
                            arrangeIssue);
                    }
                }
                var warmupResult = await ExecuteCommandAsync(
                    scenario.Command,
                    scenario.Expected,
                    executablePath,
                    context,
                    scenario.TimeoutMilliseconds,
                    cancellationToken);
                if (warmupResult.Validation.Count > 0)
                {
                    return ScenarioExecution.Invalid(
                        scenario,
                        "warmup-failure",
                        string.Join(" ", warmupResult.Validation));
                }
            }

            for (var sequence = 0; sequence < scenario.MeasurementCount; sequence++)
            {
                using var trial = scenario.ResetPolicy == "per-trial"
                    ? new TrialContext(
                        runRoot,
                        $"{scenario.ScenarioId}-sample-{sequence}",
                        datasetDirectory,
                        keepTemporary)
                    : null;
                var context = trial ?? reusedContext!;
                if (trial != null)
                {
                    var arrangeIssue = await ArrangeAsync(
                        scenario,
                        executablePath,
                        context,
                        cancellationToken);
                    if (arrangeIssue != null)
                    {
                        failures.Add(CreateFailure(
                            scenario,
                            "arrange-failure",
                            arrangeIssue));
                        break;
                    }
                }

                var execution = await ExecuteCommandAsync(
                    scenario.Command,
                    scenario.Expected,
                    executablePath,
                    context,
                    scenario.TimeoutMilliseconds,
                    cancellationToken);
                var sample = CreateSample(sequence, execution);
                result.Samples.Add(sample);
                if (sample.Status != "valid")
                {
                    failures.Add(CreateFailure(
                        scenario,
                        sample.Status,
                        string.Join(" ", sample.Validation.Issues)));
                    break;
                }
            }

            if (failures.Count == 0 && result.Samples.Count == scenario.MeasurementCount)
            {
                result.Status = "valid";
                result.Validation = new ValidationOutcome { IsValid = true };
                result.Summary = PercentileCalculator.Calculate(
                    result.Samples.Select(sample => sample.ElapsedNanoseconds!.Value));
            }
            else
            {
                var issue = failures.Count == 0
                    ? "The scenario did not produce the declared measurement count."
                    : failures[0].Message;
                result.Status = "invalid";
                result.Validation = new ValidationOutcome
                {
                    IsValid = false,
                    Issues = [issue]
                };
                if (failures.Count == 0)
                    failures.Add(CreateFailure(scenario, "sample-count", issue));
            }

            return new ScenarioExecution(result, failures);
        }
        finally
        {
            reusedContext?.Dispose();
        }
    }

    async Task<string?> ArrangeAsync(
        ScenarioDefinition scenario,
        string executablePath,
        TrialContext context,
        CancellationToken cancellationToken)
    {
        foreach (var step in scenario.Arrange)
        {
            var execution = await ExecuteCommandAsync(
                step.Command,
                step.Expected,
                executablePath,
                context,
                scenario.TimeoutMilliseconds,
                cancellationToken);
            if (execution.Validation.Count > 0)
                return string.Join(" ", execution.Validation);
        }
        return null;
    }

    async Task<ValidatedExecution> ExecuteCommandAsync(
        ProcessCommandDefinition command,
        ExpectedProcessResult expected,
        string executablePath,
        TrialContext context,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var request = context.CreateRequest(
            executablePath,
            command,
            TimeSpan.FromMilliseconds(timeoutMilliseconds));
        var execution = await _processExecutor.ExecuteAsync(request, cancellationToken);
        return new ValidatedExecution(
            execution,
            ValidateExecution(execution, expected, context));
    }

    static IReadOnlyList<string> ValidateExecution(
        ProcessExecutionResult execution,
        ExpectedProcessResult expected,
        TrialContext context)
    {
        var issues = new List<string>();
        if (execution.Status != ProcessExecutionStatus.Valid)
        {
            issues.Add(execution.Status == ProcessExecutionStatus.StartFailure
                ? $"Process start failed: {execution.StartFailureMessage}"
                : $"Process ended with status {execution.Status}.");
            return issues;
        }
        if (execution.ExitCode != expected.ExitCode)
        {
            issues.Add(
                $"Expected exit code {expected.ExitCode}, actual {execution.ExitCode}.");
        }
        foreach (var text in expected.StandardOutputContains)
        {
            if (!execution.StandardOutputText.Contains(text, StringComparison.Ordinal))
                issues.Add($"stdout did not contain '{text}'.");
        }
        foreach (var text in expected.StandardErrorContains)
        {
            if (!execution.StandardErrorText.Contains(text, StringComparison.Ordinal))
                issues.Add($"stderr did not contain '{text}'.");
        }
        if (expected.StandardOutputEmpty && execution.StandardOutput.Length != 0)
            issues.Add("stdout was not empty.");
        if (expected.StandardErrorEmpty && execution.StandardError.Length != 0)
            issues.Add("stderr was not empty.");
        foreach (var entry in expected.FileSystemEntries)
        {
            var actualPath = context.Expand(entry.Path);
            var exists = entry.Kind == "file"
                ? File.Exists(actualPath)
                : Directory.Exists(actualPath);
            if (exists != entry.Exists)
            {
                issues.Add(
                    $"Expected {entry.Kind} '{entry.Path}' to " +
                    $"{(entry.Exists ? "exist" : "be absent")}.");
                continue;
            }
            if (!exists || entry.Kind != "file")
                continue;
            if (entry.SizeBytes != null && new FileInfo(actualPath).Length != entry.SizeBytes)
                issues.Add($"File '{entry.Path}' did not have the expected size.");
            if (entry.Sha256 != null &&
                !string.Equals(
                    ContentHasher.HashFile(actualPath),
                    entry.Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add($"File '{entry.Path}' did not have the expected SHA-256.");
            }
        }
        return issues;
    }

    static PerformanceSample CreateSample(int sequence, ValidatedExecution execution)
    {
        var issues = execution.Validation.ToList();
        var status = execution.Execution.Status switch
        {
            ProcessExecutionStatus.Valid when issues.Count == 0 => "valid",
            ProcessExecutionStatus.Valid => "invalid",
            ProcessExecutionStatus.Timeout => "timeout",
            ProcessExecutionStatus.Cancelled => "cancelled",
            ProcessExecutionStatus.StartFailure => "start-failure",
            _ => "invalid"
        };
        return new PerformanceSample
        {
            Sequence = sequence,
            ElapsedNanoseconds = execution.Execution.ElapsedNanoseconds,
            ExitCode = execution.Execution.ExitCode,
            Status = status,
            StandardOutputBytes = execution.Execution.StandardOutput.LongLength,
            StandardErrorBytes = execution.Execution.StandardError.LongLength,
            PeakWorkingSetBytes = execution.Execution.PeakWorkingSetBytes,
            TotalProcessorTimeNanoseconds = execution.Execution.TotalProcessorTimeNanoseconds,
            SecondaryMetricUnavailableReason =
                execution.Execution.SecondaryMetricUnavailableReason,
            Validation = new ValidationOutcome
            {
                IsValid = issues.Count == 0,
                Issues = issues
            }
        };
    }

    static ScenarioResult CreateScenarioResult(ScenarioDefinition scenario)
    {
        return new ScenarioResult
        {
            ScenarioId = scenario.ScenarioId,
            ScenarioVersion = scenario.ScenarioVersion,
            CacheMode = scenario.CacheMode,
            WarmupCount = scenario.WarmupCount,
            MeasurementCount = scenario.MeasurementCount,
            TimeoutMilliseconds = scenario.TimeoutMilliseconds,
            Status = "invalid",
            Validation = new ValidationOutcome
            {
                IsValid = false,
                Issues = ["The scenario has not completed."]
            }
        };
    }

    static IReadOnlyList<ScenarioDefinition> SelectScenarios(
        ScenarioManifestDocument manifest,
        IReadOnlyList<string> requestedIds)
    {
        if (requestedIds.Count == 0)
            return manifest.Scenarios;

        var requested = requestedIds.ToHashSet(StringComparer.Ordinal);
        var missing = requested.Except(
            manifest.Scenarios.Select(scenario => scenario.ScenarioId),
            StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            throw new ContractException(
                $"Unknown scenario IDs: {string.Join(", ", missing)}.");
        }
        return manifest.Scenarios
            .Where(scenario => requested.Contains(scenario.ScenarioId))
            .ToArray();
    }

    static void ValidateOptions(
        RunOptions options,
        IReadOnlyList<ScenarioDefinition> scenarios)
    {
        if (options.DeploymentKind is not ("self-contained" or "native-aot"))
            throw new ContractException("deployment-kind must be self-contained or native-aot.");
        if (options.ArtifactInputKind is not ("publish-directory" or "extracted-archive"))
        {
            throw new ContractException(
                "artifact-input-kind must be publish-directory or extracted-archive.");
        }
        if (scenarios.Any(
            scenario => scenario.ArtifactInputKind != options.ArtifactInputKind))
        {
            throw new ContractException(
                "Every selected scenario must match the requested artifact-input-kind.");
        }
        if (options.ArtifactInputKind == "extracted-archive" && options.ArchivePath == null)
            throw new ContractException("extracted-archive measurements require --archive.");
        if (options.ArchivePath != null && !File.Exists(options.ArchivePath))
            throw new FileNotFoundException("The source archive was not found.", options.ArchivePath);
        if (File.Exists(options.OutputPath))
        {
            throw new ContractException(
                $"The output result '{options.OutputPath}' already exists.");
        }
    }

    static string ResolveDatasetDirectory(
        RunOptions options,
        DatasetManifestDocument manifest)
    {
        var directory = options.DatasetRoot ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.DatasetManifestPath!))!,
            manifest.Prepared.RelativePath);
        return Path.GetFullPath(directory);
    }

    static string? ValidateScenarioPrerequisites(
        ScenarioDefinition scenario,
        DatasetManifestDocument? dataset,
        string? datasetIssue)
    {
        if (scenario.DatasetId == null)
            return null;
        if (dataset == null)
            return $"Scenario '{scenario.ScenarioId}' requires dataset '{scenario.DatasetId}'.";
        if (dataset.DatasetId != scenario.DatasetId)
            return $"Scenario dataset '{scenario.DatasetId}' does not match '{dataset.DatasetId}'.";
        if (!dataset.Selection.Scales.Any(scale => scale.ScaleId == scenario.ScaleId))
            return $"Dataset scale '{scenario.ScaleId}' was not found.";
        return datasetIssue;
    }

    static RunFailure CreateFailure(
        ScenarioDefinition scenario,
        string code,
        string message)
    {
        return new RunFailure
        {
            Code = code,
            ScenarioId = scenario.ScenarioId,
            Message = message
        };
    }

    static string AppendIssue(string? current, string issue)
    {
        return current == null ? issue : $"{current} {issue}";
    }

    sealed record ValidatedExecution(
        ProcessExecutionResult Execution,
        IReadOnlyList<string> Validation);

    sealed record ScenarioExecution(
        ScenarioResult Result,
        IReadOnlyList<RunFailure> Failures)
    {
        internal static ScenarioExecution Invalid(
            ScenarioDefinition scenario,
            string code,
            string message)
        {
            var result = CreateScenarioResult(scenario);
            result.Validation = new ValidationOutcome
            {
                IsValid = false,
                Issues = [message]
            };
            return new ScenarioExecution(
                result,
                [CreateFailure(scenario, code, message)]);
        }
    }
}
