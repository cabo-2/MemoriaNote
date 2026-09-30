using System;
using System.IO;

namespace MemoriaNote.Archive
{
    /// <summary>Identifies a stable backup or restore operation failure.</summary>
    public enum ArchiveV1OperationErrorCode
    {
        /// <summary>The requested input file does not exist.</summary>
        InputNotFound,

        /// <summary>The source notebook is not a readable current-format notebook.</summary>
        SourceNotebookInvalid,

        /// <summary>The destination already exists and was not changed.</summary>
        DestinationConflict,

        /// <summary>The archive failed classification or validation.</summary>
        ArchiveValidationFailed,

        /// <summary>A file or stream operation failed.</summary>
        IoFailure,

        /// <summary>A generated archive or restored database failed post-write verification.</summary>
        IntegrityFailure
    }

    /// <summary>Describes an expected backup or restore failure.</summary>
    public sealed class ArchiveV1OperationError
    {
        internal ArchiveV1OperationError(
            ArchiveV1OperationErrorCode code,
            ArchiveV1ValidationReport validationReport = null)
        {
            Code = code;
            ValidationReport = validationReport;
        }

        /// <summary>Gets the stable operation error code.</summary>
        public ArchiveV1OperationErrorCode Code { get; }

        /// <summary>Gets archive validation details, when the failure involved an archive.</summary>
        public ArchiveV1ValidationReport ValidationReport { get; }
    }

    /// <summary>Contains the outcome of an archive v1 backup operation.</summary>
    public sealed class ArchiveV1BackupResult
    {
        ArchiveV1BackupResult(
            int metadataCount,
            int pageCount,
            ArchiveV1OperationError error)
        {
            MetadataCount = metadataCount;
            PageCount = pageCount;
            Error = error;
        }

        /// <summary>Gets whether the backup was published successfully.</summary>
        public bool IsSuccess => Error == null;

        /// <summary>Gets the number of archived metadata rows.</summary>
        public int MetadataCount { get; }

        /// <summary>Gets the number of archived page rows.</summary>
        public int PageCount { get; }

        /// <summary>Gets the operation error, or null after success.</summary>
        public ArchiveV1OperationError Error { get; }

        internal static ArchiveV1BackupResult Succeeded(int metadataCount, int pageCount)
        {
            return new ArchiveV1BackupResult(metadataCount, pageCount, null);
        }

        internal static ArchiveV1BackupResult Failed(
            ArchiveV1OperationErrorCode code,
            ArchiveV1ValidationReport report = null)
        {
            return new ArchiveV1BackupResult(
                0,
                0,
                new ArchiveV1OperationError(code, report));
        }
    }

    /// <summary>Contains the outcome of an archive v1 restore operation.</summary>
    public sealed class ArchiveV1RestoreResult
    {
        ArchiveV1RestoreResult(
            int metadataCount,
            int pageCount,
            ArchiveV1OperationError error)
        {
            MetadataCount = metadataCount;
            PageCount = pageCount;
            Error = error;
        }

        /// <summary>Gets whether the restored database was published successfully.</summary>
        public bool IsSuccess => Error == null;

        /// <summary>Gets the number of restored metadata rows.</summary>
        public int MetadataCount { get; }

        /// <summary>Gets the number of restored page rows.</summary>
        public int PageCount { get; }

        /// <summary>Gets the operation error, or null after success.</summary>
        public ArchiveV1OperationError Error { get; }

        internal static ArchiveV1RestoreResult Succeeded(int metadataCount, int pageCount)
        {
            return new ArchiveV1RestoreResult(metadataCount, pageCount, null);
        }

        internal static ArchiveV1RestoreResult Failed(
            ArchiveV1OperationErrorCode code,
            ArchiveV1ValidationReport report = null)
        {
            return new ArchiveV1RestoreResult(
                0,
                0,
                new ArchiveV1OperationError(code, report));
        }
    }

    /// <summary>Requests backup of one notebook to a new archive file.</summary>
    public sealed class ArchiveV1FileBackupRequest
    {
        /// <summary>Initializes a file backup request.</summary>
        public ArchiveV1FileBackupRequest(string sourceDatabasePath, string destinationArchivePath)
        {
            SourceDatabasePath = RequirePath(sourceDatabasePath, nameof(sourceDatabasePath));
            DestinationArchivePath = RequirePath(
                destinationArchivePath,
                nameof(destinationArchivePath));
        }

        /// <summary>Gets the current-format source notebook path.</summary>
        public string SourceDatabasePath { get; }

        /// <summary>Gets the archive path to create without replacement.</summary>
        public string DestinationArchivePath { get; }

        static string RequirePath(string value, string parameterName)
        {
            if (value == null)
                throw new ArgumentNullException(parameterName);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A file path is required.", parameterName);
            return value;
        }
    }

    /// <summary>Requests backup of one notebook to a caller-owned stream.</summary>
    public sealed class ArchiveV1StreamBackupRequest
    {
        /// <summary>Initializes a stream backup request.</summary>
        public ArchiveV1StreamBackupRequest(string sourceDatabasePath, Stream destination)
        {
            if (sourceDatabasePath == null)
                throw new ArgumentNullException(nameof(sourceDatabasePath));
            if (string.IsNullOrWhiteSpace(sourceDatabasePath))
            {
                throw new ArgumentException(
                    "A source database path is required.",
                    nameof(sourceDatabasePath));
            }

            SourceDatabasePath = sourceDatabasePath;
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        }

        /// <summary>Gets the current-format source notebook path.</summary>
        public string SourceDatabasePath { get; }

        /// <summary>Gets the caller-owned writable destination stream.</summary>
        public Stream Destination { get; }
    }

    /// <summary>Requests restore from an archive file to a new notebook file.</summary>
    public sealed class ArchiveV1FileRestoreRequest
    {
        /// <summary>Initializes a file restore request.</summary>
        public ArchiveV1FileRestoreRequest(string sourceArchivePath, string destinationDatabasePath)
        {
            SourceArchivePath = RequirePath(sourceArchivePath, nameof(sourceArchivePath));
            DestinationDatabasePath = RequirePath(
                destinationDatabasePath,
                nameof(destinationDatabasePath));
        }

        /// <summary>Gets the source archive path.</summary>
        public string SourceArchivePath { get; }

        /// <summary>Gets the new notebook path to create without replacement.</summary>
        public string DestinationDatabasePath { get; }

        static string RequirePath(string value, string parameterName)
        {
            if (value == null)
                throw new ArgumentNullException(parameterName);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A file path is required.", parameterName);
            return value;
        }
    }

    /// <summary>Requests restore from a caller-owned stream to a new notebook file.</summary>
    public sealed class ArchiveV1StreamRestoreRequest
    {
        /// <summary>Initializes a stream restore request.</summary>
        public ArchiveV1StreamRestoreRequest(Stream source, string destinationDatabasePath)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (destinationDatabasePath == null)
                throw new ArgumentNullException(nameof(destinationDatabasePath));
            if (string.IsNullOrWhiteSpace(destinationDatabasePath))
            {
                throw new ArgumentException(
                    "A destination database path is required.",
                    nameof(destinationDatabasePath));
            }

            DestinationDatabasePath = destinationDatabasePath;
        }

        /// <summary>Gets the caller-owned readable source stream.</summary>
        public Stream Source { get; }

        /// <summary>Gets the new notebook path to create without replacement.</summary>
        public string DestinationDatabasePath { get; }
    }
}
