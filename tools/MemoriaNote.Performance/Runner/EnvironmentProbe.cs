using System.Globalization;
using System.Runtime.InteropServices;
using MemoriaNote.Performance.Contracts;

namespace MemoriaNote.Performance.Runner;

internal static class EnvironmentProbe
{
    internal static EnvironmentIdentity Capture(
        string environmentLabel,
        string probePath,
        string? powerModeNote)
    {
        return new EnvironmentIdentity
        {
            EnvironmentLabel = environmentLabel,
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CpuModel = ReadCpuModel(),
            LogicalProcessorCount = Environment.ProcessorCount,
            InstalledMemoryBytes = ReadInstalledMemoryBytes(),
            FileSystem = ReadFileSystem(probePath),
            PowerModeNote = powerModeNote
        };
    }

    static string ReadCpuModel()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var line = File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(value => value.StartsWith("model name", StringComparison.Ordinal));
                var separator = line?.IndexOf(':') ?? -1;
                if (separator >= 0)
                    return line![(separator + 1)..].Trim();
            }
            catch (IOException)
            {
                // Fall back to a stable non-secret value below.
            }
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unavailable";
    }

    static long? ReadInstalledMemoryBytes()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        try
        {
            var line = File.ReadLines("/proc/meminfo")
                .FirstOrDefault(value => value.StartsWith("MemTotal:", StringComparison.Ordinal));
            var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts?.Length >= 2 &&
                long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kib))
            {
                return checked(kib * 1024);
            }
        }
        catch (IOException)
        {
            // Missing metrics are represented as null.
        }
        return null;
    }

    static string ReadFileSystem(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var drive = DriveInfo.GetDrives()
                .Where(candidate => fullPath.StartsWith(
                    candidate.RootDirectory.FullName,
                    StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(drive?.DriveFormat)
                ? "unavailable"
                : drive.DriveFormat;
        }
        catch (IOException)
        {
            return "unavailable";
        }
    }
}
