namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class CliProcessResult
{
    internal CliProcessResult(
        int exitCode,
        byte[] standardOutputBytes,
        string standardError)
    {
        ExitCode = exitCode;
        StandardOutputBytes = standardOutputBytes ??
            throw new ArgumentNullException(nameof(standardOutputBytes));
        StandardOutput = System.Text.Encoding.UTF8.GetString(StandardOutputBytes);
        StandardError = standardError;
    }

    internal int ExitCode { get; }

    internal string StandardOutput { get; }

    internal byte[] StandardOutputBytes { get; }

    internal string StandardError { get; }
}
