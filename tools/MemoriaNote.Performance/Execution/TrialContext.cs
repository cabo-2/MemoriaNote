using MemoriaNote.Performance.Contracts;

namespace MemoriaNote.Performance.Execution;

internal sealed class TrialContext : IDisposable
{
    readonly bool _keep;

    internal TrialContext(
        string parentDirectory,
        string name,
        string? datasetDirectory,
        bool keep)
    {
        RootDirectory = Path.Combine(parentDirectory, name);
        WorkingDirectory = Path.Combine(RootDirectory, "working");
        WorkspaceDirectory = Path.Combine(RootDirectory, "workspace");
        ApplicationDataDirectory = Path.Combine(RootDirectory, "application-data");
        TemporaryDirectory = Path.Combine(RootDirectory, "temp");
        DotNetHomeDirectory = Path.Combine(RootDirectory, "dotnet-home");
        DatasetDirectory = datasetDirectory;
        _keep = keep;

        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(WorkspaceDirectory);
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
        Directory.CreateDirectory(DotNetHomeDirectory);
    }

    internal string RootDirectory { get; }

    internal string WorkingDirectory { get; }

    internal string WorkspaceDirectory { get; }

    internal string ApplicationDataDirectory { get; }

    internal string TemporaryDirectory { get; }

    internal string DotNetHomeDirectory { get; }

    internal string? DatasetDirectory { get; }

    internal ProcessExecutionRequest CreateRequest(
        string executablePath,
        ProcessCommandDefinition command,
        TimeSpan timeout)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["APPDATA"] = ApplicationDataDirectory,
            ["XDG_CONFIG_HOME"] = ApplicationDataDirectory,
            ["MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY"] = ApplicationDataDirectory,
            ["DOTNET_CLI_HOME"] = DotNetHomeDirectory,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["TMPDIR"] = TemporaryDirectory,
            ["TMP"] = TemporaryDirectory,
            ["TEMP"] = TemporaryDirectory
        };
        foreach (var variable in command.Environment)
            environment[variable.Key] = Expand(variable.Value);

        return new ProcessExecutionRequest(
            executablePath,
            command.Arguments.Select(Expand).ToArray(),
            command.StandardInput == null ? null : Expand(command.StandardInput),
            Expand(command.WorkingDirectory),
            environment,
            timeout);
    }

    internal string Expand(string value)
    {
        var expanded = value
            .Replace("{root}", RootDirectory, StringComparison.Ordinal)
            .Replace("{working}", WorkingDirectory, StringComparison.Ordinal)
            .Replace("{workspace}", WorkspaceDirectory, StringComparison.Ordinal)
            .Replace("{applicationData}", ApplicationDataDirectory, StringComparison.Ordinal)
            .Replace("{temp}", TemporaryDirectory, StringComparison.Ordinal);
        if (expanded.Contains("{dataset}", StringComparison.Ordinal))
        {
            if (DatasetDirectory == null)
            {
                throw new InvalidOperationException(
                    "The scenario used {dataset} without a dataset input.");
            }
            expanded = expanded.Replace(
                "{dataset}",
                DatasetDirectory,
                StringComparison.Ordinal);
        }
        return expanded;
    }

    public void Dispose()
    {
        if (!_keep && Directory.Exists(RootDirectory))
            Directory.Delete(RootDirectory, recursive: true);
    }
}
