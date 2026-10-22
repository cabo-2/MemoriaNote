using System.Reflection;

namespace MemoriaNote.Performance.Runner;

internal static class RunnerIdentity
{
    internal static (string Version, string Commit) Read()
    {
        var assembly = typeof(RunnerIdentity).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return separator < 0
            ? (informationalVersion, "unavailable")
            : (informationalVersion[..separator], informationalVersion[(separator + 1)..]);
    }
}
