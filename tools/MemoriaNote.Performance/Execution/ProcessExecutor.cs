using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MemoriaNote.Performance.Execution;

internal sealed class ProcessExecutor
{
    internal async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request)
        };
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!process.Start())
            {
                return ProcessExecutionResult.StartFailure(
                    "The operating system declined to start the process.");
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return ProcessExecutionResult.StartFailure(
                $"The process could not be started ({exception.GetType().Name}).");
        }

        var standardOutputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var standardErrorTask = ReadAllBytesAsync(process.StandardError.BaseStream);
        using var timeoutCancellation = new CancellationTokenSource(request.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        var status = ProcessExecutionStatus.Valid;
        try
        {
            if (request.StandardInput != null)
            {
                var bytes = Encoding.UTF8.GetBytes(request.StandardInput);
                await process.StandardInput.BaseStream.WriteAsync(
                    bytes,
                    linkedCancellation.Token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            status = cancellationToken.IsCancellationRequested
                ? ProcessExecutionStatus.Cancelled
                : ProcessExecutionStatus.Timeout;
            TryCloseInput(process);
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        var stopped = Stopwatch.GetTimestamp();
        var elapsed = checked((long)Math.Round(
            Stopwatch.GetElapsedTime(started, stopped).TotalNanoseconds,
            MidpointRounding.AwayFromZero));
        var secondaryMetrics = ReadSecondaryMetrics(process);

        return new ProcessExecutionResult(
            status,
            elapsed,
            process.ExitCode,
            standardOutput,
            standardError,
            secondaryMetrics.PeakWorkingSetBytes,
            secondaryMetrics.TotalProcessorTimeNanoseconds,
            secondaryMetrics.UnavailableReason,
            null);
    }

    static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var variable in request.Environment)
            startInfo.Environment[variable.Key] = variable.Value;
        return startInfo;
    }

    static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    static void TryCloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process closed its input while cancellation was observed.
        }
    }

    static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process exited between cancellation and the kill request.
        }
    }

    static SecondaryMetrics ReadSecondaryMetrics(Process process)
    {
        try
        {
            process.Refresh();
            return new SecondaryMetrics(
                process.PeakWorkingSet64,
                checked(process.TotalProcessorTime.Ticks * 100),
                null);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return new SecondaryMetrics(
                null,
                null,
                $"Process metrics unavailable ({exception.GetType().Name}).");
        }
    }

    readonly record struct SecondaryMetrics(
        long? PeakWorkingSetBytes,
        long? TotalProcessorTimeNanoseconds,
        string? UnavailableReason);
}

internal sealed record ProcessExecutionRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

internal enum ProcessExecutionStatus
{
    Valid,
    Timeout,
    Cancelled,
    StartFailure
}

internal sealed record ProcessExecutionResult(
    ProcessExecutionStatus Status,
    long? ElapsedNanoseconds,
    int? ExitCode,
    byte[] StandardOutput,
    byte[] StandardError,
    long? PeakWorkingSetBytes,
    long? TotalProcessorTimeNanoseconds,
    string? SecondaryMetricUnavailableReason,
    string? StartFailureMessage)
{
    internal string StandardOutputText => Encoding.UTF8.GetString(StandardOutput);

    internal string StandardErrorText => Encoding.UTF8.GetString(StandardError);

    internal static ProcessExecutionResult StartFailure(string message)
    {
        return new ProcessExecutionResult(
            ProcessExecutionStatus.StartFailure,
            null,
            null,
            [],
            [],
            null,
            null,
            "The process did not start.",
            message);
    }
}
