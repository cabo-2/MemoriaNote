using System;
using System.IO;
using System.Text;
using MemoriaNote.Domain;
using Tomlyn;
using Tomlyn.Model;

namespace MemoriaNote.Persistence
{
    /// <summary>Identifies whether workspace configuration was present on disk.</summary>
    public enum WorkspaceConfigurationLoadStatus
    {
        /// <summary>No workspace configuration file exists.</summary>
        Missing,

        /// <summary>An existing workspace configuration file was loaded.</summary>
        Loaded
    }

    /// <summary>Contains a workspace configuration and its load status.</summary>
    public sealed class WorkspaceConfigurationLoadResult
    {
        /// <summary>Initializes a workspace configuration load result.</summary>
        /// <param name="configuration">The loaded or implicit-root configuration.</param>
        /// <param name="status">Whether a file was loaded.</param>
        public WorkspaceConfigurationLoadResult(
            WorkspaceConfiguration configuration,
            WorkspaceConfigurationLoadStatus status)
        {
            Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            Status = status;
        }

        /// <summary>Gets the loaded or implicit-root configuration.</summary>
        public WorkspaceConfiguration Configuration { get; }

        /// <summary>Gets whether a file was loaded.</summary>
        public WorkspaceConfigurationLoadStatus Status { get; }
    }

    /// <summary>Loads and atomically saves workspace-local selection state.</summary>
    public interface IWorkspaceConfigurationStore
    {
        /// <summary>Loads configuration without creating or modifying a file.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <returns>The existing configuration or an implicit root configuration.</returns>
        WorkspaceConfigurationLoadResult Load(string workspacePath);

        /// <summary>Atomically saves complete workspace configuration.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <param name="configuration">The complete configuration to save.</param>
        void Save(string workspacePath, WorkspaceConfiguration configuration);
    }

    /// <summary>Represents malformed or semantically invalid workspace configuration.</summary>
    public class WorkspaceConfigurationFormatException : Exception
    {
        /// <summary>Initializes a workspace configuration format exception.</summary>
        /// <param name="message">The validation message.</param>
        public WorkspaceConfigurationFormatException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a workspace configuration format exception.</summary>
        /// <param name="message">The validation message.</param>
        /// <param name="innerException">The format-specific exception.</param>
        public WorkspaceConfigurationFormatException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>Represents a workspace configuration version newer than this application.</summary>
    public sealed class UnsupportedWorkspaceConfigurationVersionException :
        WorkspaceConfigurationFormatException
    {
        /// <summary>Initializes an unsupported workspace configuration exception.</summary>
        /// <param name="formatVersion">The unsupported version.</param>
        public UnsupportedWorkspaceConfigurationVersionException(long formatVersion)
            : base($"The workspace configuration format version '{formatVersion}' is not supported.")
        {
            FormatVersion = formatVersion;
        }

        /// <summary>Gets the unsupported format version.</summary>
        public long FormatVersion { get; }
    }

    /// <summary>Persists <c>mn-workspace.toml</c> with atomic replacement.</summary>
    public sealed class WorkspaceConfigurationStore : IWorkspaceConfigurationStore
    {
        /// <summary>Gets the workspace configuration file name.</summary>
        public const string FileName = "mn-workspace.toml";

        const string FormatVersionKey = "format_version";
        const string CurrentNotebookKey = "current_notebook";

        static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <inheritdoc/>
        public WorkspaceConfigurationLoadResult Load(string workspacePath)
        {
            var configurationPath = GetConfigurationPath(workspacePath);
            RejectSymbolicLink(configurationPath);
            if (!File.Exists(configurationPath))
            {
                return new WorkspaceConfigurationLoadResult(
                    WorkspaceConfiguration.CreateRoot(),
                    WorkspaceConfigurationLoadStatus.Missing);
            }

            string content;
            try
            {
                content = File.ReadAllText(configurationPath, StrictUtf8);
            }
            catch (DecoderFallbackException exception)
            {
                throw new WorkspaceConfigurationFormatException(
                    "The workspace configuration is not valid UTF-8.",
                    exception);
            }

            return new WorkspaceConfigurationLoadResult(
                Deserialize(content),
                WorkspaceConfigurationLoadStatus.Loaded);
        }

        /// <inheritdoc/>
        public void Save(
            string workspacePath,
            WorkspaceConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var configurationPath = GetConfigurationPath(workspacePath);
            RejectSymbolicLink(configurationPath);
            var serializedConfiguration = Serialize(configuration);
            var temporaryPath = Path.Combine(
                workspacePath,
                $".{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                WriteTemporaryFile(temporaryPath, serializedConfiguration);
                if (File.Exists(configurationPath))
                {
                    File.Replace(temporaryPath, configurationPath, null);
                }
                else
                {
                    try
                    {
                        File.Move(temporaryPath, configurationPath);
                    }
                    catch (IOException) when (File.Exists(configurationPath))
                    {
                        File.Replace(temporaryPath, configurationPath, null);
                    }
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        static string GetConfigurationPath(string workspacePath)
        {
            if (workspacePath == null)
                throw new ArgumentNullException(nameof(workspacePath));
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    "A workspace path is required.",
                    nameof(workspacePath));
            }

            var normalizedWorkspacePath = Path.GetFullPath(workspacePath);
            if (!Directory.Exists(normalizedWorkspacePath))
            {
                throw new DirectoryNotFoundException(
                    $"The workspace directory does not exist: {normalizedWorkspacePath}");
            }

            return Path.Combine(normalizedWorkspacePath, FileName);
        }

        static WorkspaceConfiguration Deserialize(string content)
        {
            TomlTable table;
            try
            {
                table = TomlSerializer.Deserialize<TomlTable>(content) ??
                    throw new WorkspaceConfigurationFormatException(
                        "The workspace configuration did not contain a TOML document.");
            }
            catch (TomlException exception)
            {
                throw new WorkspaceConfigurationFormatException(
                    "The workspace configuration TOML is invalid.",
                    exception);
            }

            if (!table.TryGetValue(FormatVersionKey, out var formatVersionValue) ||
                formatVersionValue is not long formatVersion)
            {
                throw new WorkspaceConfigurationFormatException(
                    $"The workspace configuration requires an integer '{FormatVersionKey}'.");
            }
            if (formatVersion > WorkspaceConfiguration.CurrentFormatVersion)
                throw new UnsupportedWorkspaceConfigurationVersionException(formatVersion);
            if (formatVersion != WorkspaceConfiguration.CurrentFormatVersion)
            {
                throw new WorkspaceConfigurationFormatException(
                    $"The workspace configuration format version '{formatVersion}' is invalid.");
            }

            if (!table.TryGetValue(CurrentNotebookKey, out var currentNotebookValue))
                return WorkspaceConfiguration.CreateRoot();
            if (currentNotebookValue is not string currentNotebookText)
            {
                throw new WorkspaceConfigurationFormatException(
                    $"The workspace configuration field '{CurrentNotebookKey}' must be a string.");
            }

            try
            {
                return WorkspaceConfiguration.CreateSelected(
                    NotebookFileName.FromStoredValue(currentNotebookText));
            }
            catch (ArgumentException exception)
            {
                throw new WorkspaceConfigurationFormatException(
                    $"The workspace configuration field '{CurrentNotebookKey}' is invalid: " +
                    exception.Message,
                    exception);
            }
        }

        static string Serialize(WorkspaceConfiguration configuration)
        {
            var table = new TomlTable
            {
                [FormatVersionKey] = configuration.FormatVersion
            };
            if (configuration.CurrentNotebook != null)
                table[CurrentNotebookKey] = configuration.CurrentNotebook.Value;

            var serialized = TomlSerializer.Serialize(table)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd('\n');
            return serialized + "\n";
        }

        static void RejectSymbolicLink(string configurationPath)
        {
            var file = new FileInfo(configurationPath);
            if (file.LinkTarget != null)
            {
                throw new WorkspaceConfigurationFormatException(
                    $"The workspace configuration cannot be a symbolic link: {configurationPath}");
            }
        }

        static void WriteTemporaryFile(string temporaryPath, string content)
        {
            using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var writer = new StreamWriter(stream, StrictUtf8);
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }
}
