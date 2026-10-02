using System;
using MemoriaNote.Archive;

namespace MemoriaNote.Cli
{
    internal static class ArchiveV1CommandResultMapper
    {
        internal static CliCommandResult ToCliResult(ArchiveV1OperationError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            return error.Code switch
            {
                ArchiveV1OperationErrorCode.InputNotFound => Failure(
                    CliErrorKind.NotFound,
                    "The archive input or source notebook does not exist."),
                ArchiveV1OperationErrorCode.SourceNotebookInvalid => Failure(
                    CliErrorKind.Validation,
                    "The source notebook is not a valid current-format notebook."),
                ArchiveV1OperationErrorCode.DestinationConflict => Failure(
                    CliErrorKind.Conflict,
                    "The archive destination already exists."),
                ArchiveV1OperationErrorCode.ArchiveValidationFailed => Failure(
                    CliErrorKind.Validation,
                    "The input is not a valid supported archive."),
                ArchiveV1OperationErrorCode.IoFailure => Failure(
                    CliErrorKind.Storage,
                    "The archive operation failed due to an I/O error."),
                ArchiveV1OperationErrorCode.IntegrityFailure => Failure(
                    CliErrorKind.Storage,
                    "The archive failed integrity verification."),
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }

        static CliCommandResult Failure(CliErrorKind kind, string message)
        {
            return CliCommandResult.Failure(kind, message);
        }
    }
}
