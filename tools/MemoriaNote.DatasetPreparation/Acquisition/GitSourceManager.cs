using System.Diagnostics;
using MemoriaNote.DatasetPreparation.Contracts;

namespace MemoriaNote.DatasetPreparation.Acquisition;

internal sealed class GitSourceManager
{
    internal async Task<string> AcquireAsync(
        GitDatasetSource source,
        string sourceDirectory,
        CancellationToken token)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            Directory.CreateDirectory(sourceDirectory);
            try
            {
                await RunGitAsync(sourceDirectory, token, "init");
                await RunGitAsync(sourceDirectory, token, "remote", "add", "origin", source.Url);
                await RunGitAsync(sourceDirectory, token, "sparse-checkout", "init", "--no-cone");
                var patterns = source.SparsePaths
                    .Select(path => path == source.ContentRoot
                        ? "/" + path.Trim('/') + "/"
                        : "/" + path.Trim('/'))
                    .ToArray();
                await RunGitAsync(
                    sourceDirectory,
                    token,
                    ["sparse-checkout", "set", "--no-cone", .. patterns]);
                await RunGitAsync(
                    sourceDirectory,
                    token,
                    "fetch",
                    "--depth=1",
                    "--filter=blob:none",
                    "origin",
                    source.Revision);
                await RunGitAsync(sourceDirectory, token, "checkout", "--detach", "FETCH_HEAD");
            }
            catch
            {
                Directory.Delete(sourceDirectory, recursive: true);
                throw;
            }
        }

        var revision = (await RunGitAsync(
            sourceDirectory,
            token,
            "rev-parse",
            "HEAD")).Trim();
        if (!string.Equals(revision, source.Revision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Cached Git revision '{revision}' does not match '{source.Revision}'.");
        }
        return sourceDirectory;
    }

    internal async Task VerifyRevisionAsync(
        string sourceDirectory,
        string expectedRevision,
        CancellationToken token)
    {
        var revision = (await RunGitAsync(
            sourceDirectory,
            token,
            "rev-parse",
            "HEAD")).Trim();
        if (!string.Equals(revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Source revision '{revision}' does not match '{expectedRevision}'.");
        }
    }

    static async Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken token,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The git process could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments[0]} failed with exit code {process.ExitCode}: {error.Trim()}");
        }
        return output;
    }
}
