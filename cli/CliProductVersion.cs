using System;
using System.Reflection;

namespace MemoriaNote.Cli
{
    /// <summary>Provides the product version embedded in the CLI assembly.</summary>
    internal static class CliProductVersion
    {
        /// <summary>Gets the informational version used by public CLI contracts.</summary>
        internal static string InformationalVersion { get; } =
            typeof(CliProductVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ??
            throw new InvalidOperationException(
                "The CLI assembly does not define an informational version.");

        /// <summary>Gets the single-line public version display.</summary>
        internal static string Display => $"mn {InformationalVersion}";
    }
}
