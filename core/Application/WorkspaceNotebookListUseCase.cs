using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Persistence;

namespace MemoriaNote.Application
{
    /// <summary>Identifies the validation state of a workspace notebook entry.</summary>
    public enum WorkspaceNotebookStatusKind
    {
        /// <summary>The notebook uses the current format and permits changes.</summary>
        Ready,

        /// <summary>The notebook uses the current format but permits read operations only.</summary>
        ReadOnly,

        /// <summary>The notebook entry or saved selection does not resolve to a file.</summary>
        Missing,

        /// <summary>The file is not a valid current Memoria Note notebook.</summary>
        Invalid,

        /// <summary>The notebook uses a format version newer than this application supports.</summary>
        Unsupported
    }

    /// <summary>Identifies the filesystem representation of a workspace notebook entry.</summary>
    public enum WorkspaceNotebookEntryKind
    {
        /// <summary>The entry is a regular file.</summary>
        File,

        /// <summary>The entry is a symbolic link.</summary>
        SymbolicLink
    }

    /// <summary>Contains one validated entry in a workspace notebook listing.</summary>
    public sealed class WorkspaceNotebookListEntry
    {
        internal WorkspaceNotebookListEntry(
            string fileName,
            bool isCurrent,
            WorkspaceNotebookStatusKind status,
            WorkspaceNotebookEntryKind? entryKind,
            string formatVersion)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            IsCurrent = isCurrent;
            Status = status;
            EntryKind = entryKind;
            FormatVersion = formatVersion;
        }

        /// <summary>Gets the workspace-root notebook leaf file name.</summary>
        public string FileName { get; }

        /// <summary>Gets whether this entry is the saved current notebook.</summary>
        public bool IsCurrent { get; }

        /// <summary>Gets the validation state of this entry.</summary>
        public WorkspaceNotebookStatusKind Status { get; }

        /// <summary>
        /// Gets the filesystem entry kind, or null when a missing saved selection has no entry.
        /// </summary>
        public WorkspaceNotebookEntryKind? EntryKind { get; }

        /// <summary>Gets the recognized notebook format version, when available.</summary>
        public string FormatVersion { get; }
    }

    /// <summary>Lists and validates workspace-root notebook entries without changing them.</summary>
    public sealed class WorkspaceNotebookListUseCase
    {
        readonly IWorkspaceConfigurationStore _configurationStore;
        readonly INotebookFormatValidator _notebookFormatValidator;

        /// <summary>Initializes a workspace notebook listing use case.</summary>
        /// <param name="configurationStore">The workspace configuration store.</param>
        /// <param name="notebookFormatValidator">The live-notebook validator.</param>
        public WorkspaceNotebookListUseCase(
            IWorkspaceConfigurationStore configurationStore,
            INotebookFormatValidator notebookFormatValidator)
        {
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
            _notebookFormatValidator = notebookFormatValidator ??
                throw new ArgumentNullException(nameof(notebookFormatValidator));
        }

        /// <summary>Lists workspace-root notebooks in ordinal file-name order.</summary>
        /// <param name="workspacePath">The resolved workspace directory.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validated notebook entries, including a missing saved selection.</returns>
        public async Task<IReadOnlyList<WorkspaceNotebookListEntry>> ListAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            if (workspacePath == null)
                throw new ArgumentNullException(nameof(workspacePath));

            cancellationToken.ThrowIfCancellationRequested();
            var normalizedWorkspacePath = Path.GetFullPath(workspacePath);
            var currentNotebook = _configurationStore
                .Load(normalizedWorkspacePath)
                .Configuration
                .CurrentNotebook;
            var candidates = EnumerateCandidates(
                normalizedWorkspacePath,
                cancellationToken);
            if (currentNotebook != null && !candidates.ContainsKey(currentNotebook.Value))
            {
                candidates.Add(
                    currentNotebook.Value,
                    CreateSavedSelectionCandidate(
                        normalizedWorkspacePath,
                        currentNotebook.Value));
            }

            var entries = new List<WorkspaceNotebookListEntry>(candidates.Count);
            foreach (var candidate in candidates.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(await ValidateAsync(
                        candidate,
                        currentNotebook,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            return entries.AsReadOnly();
        }

        static SortedDictionary<string, NotebookCandidate> EnumerateCandidates(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            var candidates = new SortedDictionary<string, NotebookCandidate>(
                StringComparer.Ordinal);
            var workspace = new DirectoryInfo(workspacePath);
            foreach (var entry in workspace.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Name.EndsWith(NotebookFileName.Extension, StringComparison.Ordinal))
                    continue;

                var isSymbolicLink = entry.LinkTarget != null;
                if ((entry.Attributes & FileAttributes.Directory) != 0 ||
                    (isSymbolicLink && Directory.Exists(entry.FullName)))
                {
                    continue;
                }
                if (!TryValidateCandidateName(entry.Name))
                    continue;

                candidates.Add(
                    entry.Name,
                    new NotebookCandidate(
                        entry.Name,
                        entry.FullName,
                        isSymbolicLink
                            ? WorkspaceNotebookEntryKind.SymbolicLink
                            : WorkspaceNotebookEntryKind.File));
            }

            return candidates;
        }

        static bool TryValidateCandidateName(string fileName)
        {
            try
            {
                NotebookFileName.FromStoredValue(fileName);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static NotebookCandidate CreateSavedSelectionCandidate(
            string workspacePath,
            string fileName)
        {
            var path = Path.Combine(workspacePath, fileName);
            var file = new FileInfo(path);
            WorkspaceNotebookEntryKind? entryKind = null;
            if (file.LinkTarget != null)
                entryKind = WorkspaceNotebookEntryKind.SymbolicLink;
            else if (File.Exists(path))
                entryKind = WorkspaceNotebookEntryKind.File;

            return new NotebookCandidate(fileName, path, entryKind);
        }

        async Task<WorkspaceNotebookListEntry> ValidateAsync(
            NotebookCandidate candidate,
            NotebookFileName currentNotebook,
            CancellationToken cancellationToken)
        {
            var isCurrent = string.Equals(
                candidate.FileName,
                currentNotebook?.Value,
                StringComparison.Ordinal);
            if (!CandidateExists(candidate))
            {
                return CreateEntry(
                    candidate,
                    isCurrent,
                    WorkspaceNotebookStatusKind.Missing);
            }

            try
            {
                var metadata = await _notebookFormatValidator
                    .ValidateCurrentAsync(candidate.Path, cancellationToken)
                    .ConfigureAwait(false);
                return CreateEntry(
                    candidate,
                    isCurrent,
                    metadata.Metadata.ReadOnly
                        ? WorkspaceNotebookStatusKind.ReadOnly
                        : WorkspaceNotebookStatusKind.Ready,
                    metadata.Metadata.Version);
            }
            catch (UnsupportedNotebookFormatVersionException exception)
            {
                return CreateEntry(
                    candidate,
                    isCurrent,
                    WorkspaceNotebookStatusKind.Unsupported,
                    exception.FormatVersion);
            }
            catch (FileNotFoundException)
            {
                return CreateEntry(
                    candidate,
                    isCurrent,
                    WorkspaceNotebookStatusKind.Missing);
            }
            catch (InvalidDataException)
            {
                return CreateEntry(
                    candidate,
                    isCurrent,
                    WorkspaceNotebookStatusKind.Invalid);
            }
        }

        static bool CandidateExists(NotebookCandidate candidate)
        {
            if (candidate.EntryKind != WorkspaceNotebookEntryKind.SymbolicLink)
                return File.Exists(candidate.Path);

            var resolvedTarget = new FileInfo(candidate.Path)
                .ResolveLinkTarget(returnFinalTarget: true);
            return resolvedTarget is FileInfo targetFile && targetFile.Exists;
        }

        static WorkspaceNotebookListEntry CreateEntry(
            NotebookCandidate candidate,
            bool isCurrent,
            WorkspaceNotebookStatusKind status,
            string formatVersion = null)
        {
            return new WorkspaceNotebookListEntry(
                candidate.FileName,
                isCurrent,
                status,
                candidate.EntryKind,
                formatVersion);
        }

        sealed class NotebookCandidate
        {
            internal NotebookCandidate(
                string fileName,
                string path,
                WorkspaceNotebookEntryKind? entryKind)
            {
                FileName = fileName;
                Path = path;
                EntryKind = entryKind;
            }

            internal string FileName { get; }

            internal string Path { get; }

            internal WorkspaceNotebookEntryKind? EntryKind { get; }
        }
    }
}
