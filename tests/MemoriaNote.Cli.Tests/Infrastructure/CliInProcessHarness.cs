using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using MemoriaNote.Cli;
using MemoriaNote.Cli.UserConfig;
using Microsoft.Data.Sqlite;
using UserConfigurationModel = MemoriaNote.Cli.UserConfig.UserConfiguration;

namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class CliInProcessHarness : IDisposable
{
    static readonly SemaphoreSlim InvocationGate = new(1, 1);
    static readonly string[] IsolatedEnvironmentVariableNames =
    {
        "APPDATA",
        "XDG_CONFIG_HOME",
        "MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY",
        "TMPDIR",
        "TMP",
        "TEMP"
    };

    readonly Dictionary<string, string?> _environmentVariables = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    readonly bool _preserveTestRoot;
    bool _disposed;

    internal CliInProcessHarness(bool preserveTestRoot = false)
    {
        _preserveTestRoot = preserveTestRoot;
        TestRoot = Path.Combine(
            Path.GetTempPath(),
            $"MemoriaNote.Cli.InProcess.Tests-{Guid.NewGuid():N}");
        WorkingDirectory = Path.Combine(TestRoot, "working");
        ApplicationDataRoot = Path.Combine(TestRoot, "application-data");
        ApplicationDataDirectory = Path.Combine(
            ApplicationDataRoot,
            "MemoriaNote");
        TemporaryDirectory = Path.Combine(TestRoot, "temp");

        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(ApplicationDataRoot);
        Directory.CreateDirectory(TemporaryDirectory);
    }

    internal string TestRoot { get; }

    internal string WorkingDirectory { get; }

    internal string ApplicationDataRoot { get; }

    internal string ApplicationDataDirectory { get; }

    internal string TemporaryDirectory { get; }

    internal string ConfigurationPath => Path.Combine(
        ApplicationDataDirectory,
        "configuration.json");

    internal string UserConfigurationPath => Path.Combine(
        ApplicationDataDirectory,
        "config.toml");

    internal string TestEditorExecutablePath => GetAssemblyMetadataPath(
        "TestEditorExecutablePath");

    internal Task<byte[]> ReadDatabaseBytesAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SqliteConnection.ClearAllPools();
        return File.ReadAllBytesAsync(databasePath, cancellationToken);
    }

    internal void ConfigureEnvironmentEditor(
        string variable = "EDITOR",
        IEnumerable<string>? arguments = null)
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        File.WriteAllText(
            UserConfigurationPath,
            UserConfigurationCodec.Serialize(
                new UserConfigurationModel(
                    UserEditorConfiguration.FromEnvironment(variable, arguments))),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal void SetEnvironmentVariable(string name, string? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An environment variable name is required.",
                nameof(name));
        }

        _environmentVariables[name] = value;
    }

    internal Task<CliInProcessResult> RunAsync(params string[] arguments)
    {
        return RunAsync(arguments, null);
    }

    internal async Task<CliInProcessResult> RunAsync(
        IEnumerable<string> arguments,
        string? standardInput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(arguments);
        var argumentArray = arguments.ToArray();

        await InvocationGate.WaitAsync().ConfigureAwait(false);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        var originalEnvironment = EnvironmentVariableNames().ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            _environmentVariables.Comparer);
        using var input = new StringReader(standardInput ?? string.Empty);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        CliInProcessResult? result = null;
        Exception? invocationException = null;

        try
        {
            Directory.SetCurrentDirectory(WorkingDirectory);
            SetEnvironmentVariables();
            Console.SetIn(input);
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = await Program.Main(argumentArray).ConfigureAwait(false);
            result = new CliInProcessResult(
                exitCode,
                output.ToString(),
                error.ToString());
        }
        catch (Exception exception)
        {
            invocationException = exception;
        }

        var restorationExceptions = RestoreGlobalState(
            originalInput,
            originalOutput,
            originalError,
            originalDirectory,
            originalEnvironment);
        TryRestore(SqliteConnection.ClearAllPools, restorationExceptions);
        InvocationGate.Release();

        if (invocationException != null)
        {
            if (restorationExceptions.Count > 0)
            {
                restorationExceptions.Insert(0, invocationException);
                throw new AggregateException(
                    "The CLI invocation and global state restoration both failed.",
                    restorationExceptions);
            }

            ExceptionDispatchInfo.Capture(invocationException).Throw();
        }

        if (restorationExceptions.Count > 0)
        {
            throw new AggregateException(
                "The CLI invocation completed, but global state restoration failed.",
                restorationExceptions);
        }

        return result!;
    }

    IEnumerable<string> EnvironmentVariableNames()
    {
        return IsolatedEnvironmentVariableNames.Concat(_environmentVariables.Keys)
            .Distinct(_environmentVariables.Comparer);
    }

    void SetEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("APPDATA", ApplicationDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", ApplicationDataRoot);
        Environment.SetEnvironmentVariable(
            "MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY",
            ApplicationDataDirectory);
        Environment.SetEnvironmentVariable("TMPDIR", TemporaryDirectory);
        Environment.SetEnvironmentVariable("TMP", TemporaryDirectory);
        Environment.SetEnvironmentVariable("TEMP", TemporaryDirectory);
        foreach (var environmentVariable in _environmentVariables)
        {
            Environment.SetEnvironmentVariable(
                environmentVariable.Key,
                environmentVariable.Value);
        }
    }

    static List<Exception> RestoreGlobalState(
        TextReader input,
        TextWriter output,
        TextWriter error,
        string currentDirectory,
        IReadOnlyDictionary<string, string?> environment)
    {
        var exceptions = new List<Exception>();
        TryRestore(() => Console.SetIn(input), exceptions);
        TryRestore(() => Console.SetOut(output), exceptions);
        TryRestore(() => Console.SetError(error), exceptions);
        TryRestore(
            () => Directory.SetCurrentDirectory(currentDirectory),
            exceptions);
        foreach (var environmentVariable in environment)
        {
            TryRestore(
                () => Environment.SetEnvironmentVariable(
                    environmentVariable.Key,
                    environmentVariable.Value),
                exceptions);
        }

        return exceptions;
    }

    static void TryRestore(Action restore, ICollection<Exception> exceptions)
    {
        try
        {
            restore();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    static string GetAssemblyMetadataPath(string key)
    {
        var path = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The '{key}' test artifact was not built before the test run.",
                path);
        }

        return path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SqliteConnection.ClearAllPools();
        if (!_preserveTestRoot && Directory.Exists(TestRoot))
            Directory.Delete(TestRoot, recursive: true);
    }
}
