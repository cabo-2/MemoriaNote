using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class CliProcessHarness : IDisposable
{
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    readonly string _testRoot;

    internal CliProcessHarness()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"MemoriaNote.Cli.Tests-{Guid.NewGuid():N}");
        WorkingDirectory = Path.Combine(_testRoot, "working");
        ApplicationDataRoot = Path.Combine(_testRoot, "application-data");
        ApplicationDataDirectory = Path.Combine(
            ApplicationDataRoot,
            "MemoriaNote");
        TemporaryDirectory = Path.Combine(_testRoot, "temp");
        DotNetHomeDirectory = Path.Combine(_testRoot, "dotnet-home");

        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(ApplicationDataRoot);
        Directory.CreateDirectory(TemporaryDirectory);
        Directory.CreateDirectory(DotNetHomeDirectory);
    }

    internal string WorkingDirectory { get; }

    internal string ApplicationDataRoot { get; }

    internal string ApplicationDataDirectory { get; }

    internal string TemporaryDirectory { get; }

    internal string DotNetHomeDirectory { get; }

    internal string ConfigurationPath => Path.Combine(
        ApplicationDataDirectory,
        "configuration.json");

    internal Task<CliProcessResult> RunAsync(params string[] arguments)
    {
        return RunAsync(arguments, null, DefaultTimeout, CancellationToken.None);
    }

    internal async Task<CliProcessResult> RunAsync(
        IEnumerable<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (arguments == null)
            throw new ArgumentNullException(nameof(arguments));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(arguments);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The CLI process could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        try
        {
            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(
                    standardInput.AsMemory(),
                    linkedCancellation.Token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The process closed its input while cancellation was being observed.
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between cancellation and the kill request.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            var output = await standardOutput;
            var error = await standardError;

            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"The CLI process exceeded the {timeout} timeout." +
                $"{Environment.NewLine}stdout:{Environment.NewLine}{output}" +
                $"{Environment.NewLine}stderr:{Environment.NewLine}{error}");
        }

        return new CliProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
                "dotnet",
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(GetCliAssemblyPath());
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment["APPDATA"] = ApplicationDataRoot;
        startInfo.Environment["XDG_CONFIG_HOME"] = ApplicationDataRoot;
        startInfo.Environment["MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY"] =
            ApplicationDataDirectory;
        startInfo.Environment["DOTNET_CLI_HOME"] = DotNetHomeDirectory;
        // cspell:disable-next-line
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        // cspell:disable-next-line
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["TMPDIR"] = TemporaryDirectory;
        startInfo.Environment["TMP"] = TemporaryDirectory;
        startInfo.Environment["TEMP"] = TemporaryDirectory;

        return startInfo;
    }

    static string GetCliAssemblyPath()
    {
        var path = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "CliAssemblyPath")
            .Value;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "The CLI assembly was not built before the test run.",
                path);
        }

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }
}
