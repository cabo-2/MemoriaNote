using System;
using MemoriaNote.Domain;

namespace MemoriaNote.Transfer
{
    /// <summary>Controls how text import handles page names already in a notebook.</summary>
    public enum TextPageImportConflictPolicy
    {
        /// <summary>Reject the complete import when any page name already exists.</summary>
        Fail,

        /// <summary>Leave existing page-name groups unchanged and import the remaining files.</summary>
        Skip,

        /// <summary>Replace the body of an existing uniquely named page.</summary>
        Replace
    }

    /// <summary>Identifies a stable text import failure.</summary>
    public enum TextPageImportErrorCode
    {
        /// <summary>The source directory or an enumerated input disappeared.</summary>
        InputNotFound,

        /// <summary>An input file is not strict UTF-8.</summary>
        InvalidEncoding,

        /// <summary>An input file decodes to an invalid page name.</summary>
        InvalidPageName,

        /// <summary>Multiple input files decode to the same exact page name.</summary>
        DuplicateInputName,

        /// <summary>An existing page name conflicts with the fail policy.</summary>
        ExistingPageConflict,

        /// <summary>More than one existing page makes replacement ambiguous.</summary>
        AmbiguousPageConflict,

        /// <summary>The target notebook does not permit writes.</summary>
        ReadOnly,

        /// <summary>A source file operation failed.</summary>
        IoFailure
    }

    /// <summary>Describes one decoded text file ready for transactional import.</summary>
    public sealed class TextPageImportItem
    {
        /// <summary>Initializes a decoded import item.</summary>
        public TextPageImportItem(string name, string text)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>Gets the exact page name.</summary>
        public string Name { get; }

        /// <summary>Gets the page body.</summary>
        public string Text { get; }
    }

    /// <summary>Requests a flat text import into one notebook.</summary>
    public sealed class TextPageImportRequest
    {
        /// <summary>Initializes a text import request.</summary>
        public TextPageImportRequest(
            NotebookId notebookId,
            string sourceDirectory,
            TextPageImportConflictPolicy conflictPolicy,
            bool dryRun = false)
        {
            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            if (sourceDirectory == null)
                throw new ArgumentNullException(nameof(sourceDirectory));
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException(
                    "A source directory is required.",
                    nameof(sourceDirectory));
            }
            if (!Enum.IsDefined(conflictPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(conflictPolicy));
            }

            SourceDirectory = sourceDirectory;
            ConflictPolicy = conflictPolicy;
            DryRun = dryRun;
        }

        /// <summary>Gets the target notebook.</summary>
        public NotebookId NotebookId { get; }

        /// <summary>Gets the source directory.</summary>
        public string SourceDirectory { get; }

        /// <summary>Gets the existing-name policy.</summary>
        public TextPageImportConflictPolicy ConflictPolicy { get; }

        /// <summary>Gets whether the operation validates without changing the notebook.</summary>
        public bool DryRun { get; }
    }

    /// <summary>Contains the classified outcome of a text import.</summary>
    public sealed class TextPageImportResult
    {
        TextPageImportResult(
            int createdCount,
            int replacedCount,
            int skippedCount,
            TextPageImportErrorCode? errorCode)
        {
            CreatedCount = createdCount;
            ReplacedCount = replacedCount;
            SkippedCount = skippedCount;
            ErrorCode = errorCode;
        }

        /// <summary>Gets whether validation and any requested writes succeeded.</summary>
        public bool IsSuccess => ErrorCode == null;

        /// <summary>Gets the number of pages created or planned for creation.</summary>
        public int CreatedCount { get; }

        /// <summary>Gets the number of pages replaced or planned for replacement.</summary>
        public int ReplacedCount { get; }

        /// <summary>Gets the number of explicitly skipped inputs.</summary>
        public int SkippedCount { get; }

        /// <summary>Gets the stable error code after failure.</summary>
        public TextPageImportErrorCode? ErrorCode { get; }

        internal static TextPageImportResult Succeeded(
            int createdCount,
            int replacedCount,
            int skippedCount)
        {
            return new TextPageImportResult(
                createdCount,
                replacedCount,
                skippedCount,
                null);
        }

        internal static TextPageImportResult Failed(TextPageImportErrorCode errorCode)
        {
            return new TextPageImportResult(0, 0, 0, errorCode);
        }
    }
}
