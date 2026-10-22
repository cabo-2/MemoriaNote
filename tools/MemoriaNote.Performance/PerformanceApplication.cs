using MemoriaNote.Performance.CommandLine;
using MemoriaNote.Performance.Contracts;
using MemoriaNote.Performance.Runner;
using MemoriaNote.Performance.Validation;

namespace MemoriaNote.Performance;

internal sealed class PerformanceApplication
{
    internal const int SuccessExitCode = 0;
    internal const int UnexpectedFailureExitCode = 1;
    internal const int UsageExitCode = 2;
    internal const int InputFailureExitCode = 3;
    internal const int InvalidResultExitCode = 4;

    readonly TextWriter _standardOutput;
    readonly TextWriter _standardError;

    internal PerformanceApplication(TextWriter standardOutput, TextWriter standardError)
    {
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    internal async Task<int> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = CommandLineParser.Parse(arguments);
            switch (command)
            {
                case HelpCommand:
                    await _standardOutput.WriteAsync(HelpText);
                    return SuccessExitCode;
                case RunCommand run:
                    var result = await new PerformanceRunner().RunAsync(
                        run.Options,
                        cancellationToken);
                    await _standardOutput.WriteLineAsync(
                        $"Performance result: {Path.GetFullPath(run.Options.OutputPath)}");
                    await _standardOutput.WriteLineAsync($"Status: {result.Run.Status}");
                    return result.Run.Status == "valid"
                        ? SuccessExitCode
                        : InvalidResultExitCode;
                case DescribeCommand describe:
                    await DescribeAsync(describe.Options, cancellationToken);
                    return SuccessExitCode;
                case ValidateResultCommand validate:
                    return await ValidateResultAsync(validate.ResultPath, cancellationToken);
                default:
                    throw new InvalidOperationException("Unknown parsed command.");
            }
        }
        catch (CommandLineException exception)
        {
            await _standardError.WriteLineAsync($"Error: {exception.Message}");
            await _standardError.WriteLineAsync("Run with --help for usage.");
            return UsageExitCode;
        }
        catch (Exception exception) when (
            exception is ContractException or IOException or UnauthorizedAccessException)
        {
            await _standardError.WriteLineAsync($"Error: {exception.Message}");
            return InputFailureExitCode;
        }
        catch (OperationCanceledException)
        {
            await _standardError.WriteLineAsync("Error: The performance run was cancelled.");
            return InvalidResultExitCode;
        }
        catch (Exception exception)
        {
            await _standardError.WriteLineAsync($"Unexpected error: {exception}");
            return UnexpectedFailureExitCode;
        }
    }

    async Task DescribeAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var manifest = await ContractLoader.ReadScenarioManifestAsync(
            options.ScenarioManifestPath,
            cancellationToken);
        var selected = options.ScenarioIds.Count == 0
            ? manifest.Scenarios
            : manifest.Scenarios
                .Where(scenario => options.ScenarioIds.Contains(
                    scenario.ScenarioId,
                    StringComparer.Ordinal))
                .ToList();
        var missing = options.ScenarioIds.Except(
            selected.Select(scenario => scenario.ScenarioId),
            StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new ContractException($"Unknown scenario IDs: {string.Join(", ", missing)}.");

        await _standardOutput.WriteLineAsync($"Artifact: {options.ArtifactId}");
        await _standardOutput.WriteLineAsync($"Executable: {Path.GetFullPath(options.ExecutablePath)}");
        await _standardOutput.WriteLineAsync($"Deployment: {options.DeploymentKind}");
        await _standardOutput.WriteLineAsync($"Input kind: {options.ArtifactInputKind}");
        if (options.DatasetManifestPath != null)
        {
            var dataset = await ContractLoader.ReadDatasetManifestAsync(
                options.DatasetManifestPath,
                cancellationToken);
            await _standardOutput.WriteLineAsync(
                $"Dataset: {dataset.DatasetId} {dataset.DatasetVersion}");
        }
        foreach (var scenario in selected)
        {
            await _standardOutput.WriteLineAsync(
                $"Scenario: {scenario.ScenarioId} " +
                $"({scenario.WarmupCount} warm-up, {scenario.MeasurementCount} measured, " +
                $"{scenario.TimeoutMilliseconds} ms timeout)");
        }
    }

    async Task<int> ValidateResultAsync(string path, CancellationToken cancellationToken)
    {
        PerformanceResultDocument result;
        try
        {
            result = await ContractLoader.ReadResultAsync(path, cancellationToken);
        }
        catch (ContractException exception)
        {
            await _standardError.WriteLineAsync($"Error: {exception.Message}");
            return InvalidResultExitCode;
        }
        var issues = ResultValidator.Validate(result);
        if (issues.Count > 0)
        {
            foreach (var issue in issues)
                await _standardError.WriteLineAsync($"Error: {issue}");
            return InvalidResultExitCode;
        }
        await _standardOutput.WriteLineAsync(
            $"Valid performance result: {Path.GetFullPath(path)}");
        return SuccessExitCode;
    }

    internal const string HelpText =
        "MemoriaNote published performance harness\n" +
        "\n" +
        "Usage:\n" +
        "  MemoriaNote.Performance run [options]\n" +
        "  MemoriaNote.Performance describe [options]\n" +
        "  MemoriaNote.Performance validate-result --result <path>\n" +
        "\n" +
        "Required run/describe options:\n" +
        "  --scenario-manifest <path>\n" +
        "  --executable <path>\n" +
        "  --artifact-id <id>\n" +
        "  --product-version <version>\n" +
        "  --source-commit <commit>\n" +
        "  --rid <rid>\n" +
        "  --deployment-kind <self-contained|native-aot>\n" +
        "  --artifact-input-kind <publish-directory|extracted-archive>\n" +
        "  --environment-label <label>\n" +
        "  --output <path>                 required only by run\n" +
        "\n" +
        "Optional:\n" +
        "  --dataset-manifest <path>\n" +
        "  --dataset-root <path>\n" +
        "  --archive <path>\n" +
        "  --configuration <name>\n" +
        "  --power-mode-note <text>\n" +
        "  --temporary-root <path>\n" +
        "  --scenario <id>                repeatable\n" +
        "  --keep-temporary\n";
}
