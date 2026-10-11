namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class CliInProcessResult
{
    internal CliInProcessResult(
        int exitCode,
        string standardOutput,
        string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ??
            throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ??
            throw new ArgumentNullException(nameof(standardError));
    }

    internal int ExitCode { get; }

    internal string StandardOutput { get; }

    internal string StandardError { get; }
}
