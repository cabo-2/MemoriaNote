using MemoriaNote.Performance.Runner;

namespace MemoriaNote.Performance.CommandLine;

internal static class CommandLineParser
{
    internal static ParsedCommand Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0 || arguments[0] is "--help" or "-h")
            return new HelpCommand();

        return arguments[0] switch
        {
            "run" => new RunCommand(ParseRunOptions(arguments[1..], requireOutput: true)),
            "describe" => new DescribeCommand(ParseRunOptions(arguments[1..], requireOutput: false)),
            "validate-result" => ParseValidateResult(arguments[1..]),
            _ => throw new CommandLineException($"Unknown command '{arguments[0]}'.")
        };
    }

    static RunOptions ParseRunOptions(string[] arguments, bool requireOutput)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var scenarios = new List<string>();
        var keepTemporary = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var name = arguments[index];
            if (name == "--keep-temporary")
            {
                keepTemporary = true;
                continue;
            }
            if (!KnownRunOptions.Contains(name))
                throw new CommandLineException($"Unknown option '{name}'.");
            if (++index >= arguments.Length)
                throw new CommandLineException($"Option '{name}' requires a value.");
            var value = arguments[index];
            if (name == "--scenario")
            {
                scenarios.Add(value);
            }
            else if (!values.TryAdd(name, value))
            {
                throw new CommandLineException($"Option '{name}' was specified more than once.");
            }
        }

        return new RunOptions
        {
            ScenarioManifestPath = Required(values, "--scenario-manifest"),
            DatasetManifestPath = Optional(values, "--dataset-manifest"),
            DatasetRoot = Optional(values, "--dataset-root"),
            ExecutablePath = Required(values, "--executable"),
            ArtifactId = Required(values, "--artifact-id"),
            ProductVersion = Required(values, "--product-version"),
            SourceCommit = Required(values, "--source-commit"),
            Rid = Required(values, "--rid"),
            Configuration = Optional(values, "--configuration") ?? "Release",
            DeploymentKind = Required(values, "--deployment-kind"),
            ArtifactInputKind = Required(values, "--artifact-input-kind"),
            ArchivePath = Optional(values, "--archive"),
            EnvironmentLabel = Required(values, "--environment-label"),
            PowerModeNote = Optional(values, "--power-mode-note"),
            OutputPath = requireOutput
                ? Required(values, "--output")
                : Optional(values, "--output") ?? "unused.json",
            TemporaryRoot = Optional(values, "--temporary-root"),
            KeepTemporary = keepTemporary,
            ScenarioIds = scenarios
        };
    }

    static ValidateResultCommand ParseValidateResult(string[] arguments)
    {
        if (arguments.Length != 2 || arguments[0] != "--result")
            throw new CommandLineException("validate-result requires --result <path>.");
        return new ValidateResultCommand(arguments[1]);
    }

    static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        var value = Optional(values, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new CommandLineException($"Required option '{name}' is missing.");
        return value;
    }

    static string? Optional(IReadOnlyDictionary<string, string> values, string name)
    {
        return values.GetValueOrDefault(name);
    }

    static readonly HashSet<string> KnownRunOptions =
    [
        "--scenario-manifest",
        "--dataset-manifest",
        "--dataset-root",
        "--executable",
        "--artifact-id",
        "--product-version",
        "--source-commit",
        "--rid",
        "--configuration",
        "--deployment-kind",
        "--artifact-input-kind",
        "--archive",
        "--environment-label",
        "--power-mode-note",
        "--output",
        "--temporary-root",
        "--scenario"
    ];
}

internal abstract record ParsedCommand;

internal sealed record HelpCommand : ParsedCommand;

internal sealed record RunCommand(RunOptions Options) : ParsedCommand;

internal sealed record DescribeCommand(RunOptions Options) : ParsedCommand;

internal sealed record ValidateResultCommand(string ResultPath) : ParsedCommand;

internal sealed class CommandLineException : Exception
{
    internal CommandLineException(string message)
        : base(message)
    {
    }
}
