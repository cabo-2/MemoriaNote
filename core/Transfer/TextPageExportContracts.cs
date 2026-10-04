using System;
using MemoriaNote.Domain;

namespace MemoriaNote.Transfer
{
    /// <summary>Controls how text export handles page names that share an output path.</summary>
    public enum TextPageExportNameConflictPolicy
    {
        /// <summary>Reject the complete export when any output path conflicts.</summary>
        Fail,

        /// <summary>Add a stable Page ID suffix to every member of a conflicting group.</summary>
        IdSuffix
    }

    /// <summary>Identifies a stable text export failure.</summary>
    public enum TextPageExportErrorCode
    {
        /// <summary>The destination path already exists.</summary>
        DestinationConflict,

        /// <summary>One or more page names cannot produce unique, safe output paths.</summary>
        NameConflict,

        /// <summary>A destination file operation failed.</summary>
        IoFailure
    }

    /// <summary>Requests a flat text export from one notebook.</summary>
    public sealed class TextPageExportRequest
    {
        /// <summary>Initializes a text export request.</summary>
        public TextPageExportRequest(
            NotebookId notebookId,
            string destinationDirectory,
            TextPageExportNameConflictPolicy nameConflictPolicy)
        {
            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            if (destinationDirectory == null)
                throw new ArgumentNullException(nameof(destinationDirectory));
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException(
                    "A destination directory is required.",
                    nameof(destinationDirectory));
            }
            if (!Enum.IsDefined(nameConflictPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(nameConflictPolicy));
            }

            DestinationDirectory = destinationDirectory;
            NameConflictPolicy = nameConflictPolicy;
        }

        /// <summary>Gets the source notebook.</summary>
        public NotebookId NotebookId { get; }

        /// <summary>Gets the new destination directory.</summary>
        public string DestinationDirectory { get; }

        /// <summary>Gets the output-path conflict policy.</summary>
        public TextPageExportNameConflictPolicy NameConflictPolicy { get; }
    }

    /// <summary>Contains the classified outcome of a text export.</summary>
    public sealed class TextPageExportResult
    {
        TextPageExportResult(int exportedCount, TextPageExportErrorCode? errorCode)
        {
            ExportedCount = exportedCount;
            ErrorCode = errorCode;
        }

        /// <summary>Gets whether every page was published successfully.</summary>
        public bool IsSuccess => ErrorCode == null;

        /// <summary>Gets the number of exported pages.</summary>
        public int ExportedCount { get; }

        /// <summary>Gets the stable error code after failure.</summary>
        public TextPageExportErrorCode? ErrorCode { get; }

        internal static TextPageExportResult Succeeded(int exportedCount)
        {
            return new TextPageExportResult(exportedCount, null);
        }

        internal static TextPageExportResult Failed(TextPageExportErrorCode errorCode)
        {
            return new TextPageExportResult(0, errorCode);
        }
    }
}
