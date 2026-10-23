namespace MemoriaNote.DatasetPreparation.CommandLine;

internal static class CommandLineParser
{
    internal static DatasetCommand Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0 || arguments[0] is "--help" or "-h")
            return new HelpCommand();
        if (arguments[0] == "verify")
            return new VerifyCommand(ParseOptions(arguments[1..], allowUpdateLock: false));
        if (arguments[0] == "inventory")
            return new InventoryCommand(ParseOptions(arguments[1..], allowUpdateLock: false));
        if (arguments[0] == "prepare")
        {
            var options = ParseOptions(arguments[1..], allowUpdateLock: true);
            return new PrepareCommand(options);
        }
        throw new CommandLineException($"Unknown command '{arguments[0]}'.");
    }

    static DatasetOptions ParseOptions(string[] arguments, bool allowUpdateLock)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var updateLock = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var name = arguments[index];
            if (name == "--update-lock")
            {
                if (!allowUpdateLock)
                    throw new CommandLineException("--update-lock is not valid for verify.");
                updateLock = true;
                continue;
            }
            if (name is not ("--manifest" or "--cache-root" or "--source-root" or "--dataset-root"))
                throw new CommandLineException($"Unknown option '{name}'.");
            if (++index >= arguments.Length)
                throw new CommandLineException($"Option '{name}' requires a value.");
            if (!values.TryAdd(name, arguments[index]))
                throw new CommandLineException($"Option '{name}' was specified more than once.");
        }
        return new DatasetOptions(
            Required(values, "--manifest"),
            Required(values, "--cache-root"),
            values.GetValueOrDefault("--source-root"),
            values.GetValueOrDefault("--dataset-root"),
            updateLock);
    }

    static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        var value = values.GetValueOrDefault(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new CommandLineException($"Required option '{name}' is missing.");
        return value;
    }
}

internal abstract record DatasetCommand;

internal sealed record HelpCommand : DatasetCommand;

internal sealed record InventoryCommand(DatasetOptions Options) : DatasetCommand;

internal sealed record PrepareCommand(DatasetOptions Options) : DatasetCommand;

internal sealed record VerifyCommand(DatasetOptions Options) : DatasetCommand;

internal sealed record DatasetOptions(
    string ManifestPath,
    string CacheRoot,
    string? SourceRoot,
    string? DatasetRoot,
    bool UpdateLock);

internal sealed class CommandLineException : Exception
{
    internal CommandLineException(string message) : base(message)
    {
    }
}
