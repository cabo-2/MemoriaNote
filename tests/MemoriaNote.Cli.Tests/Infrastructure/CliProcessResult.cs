namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class CliProcessResult
{
    internal CliProcessResult(
        int exitCode,
        string standardOutput,
        string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    internal int ExitCode { get; }

    internal string StandardOutput { get; }

    internal string StandardError { get; }
}
