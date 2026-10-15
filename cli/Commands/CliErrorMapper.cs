using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.UserConfig;
using MemoriaNote.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote.Cli
{
    internal sealed class CliErrorMapper
    {
        internal CliCommandResult Map(
            Exception exception,
            bool cancellationRequested)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            if (exception is OperationCanceledException && cancellationRequested)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Canceled,
                    "The operation was interrupted.",
                    exception);
            }

            if (exception is NotebookTargetNotFoundException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    exception.Message,
                    exception);
            }

            if (exception is NotebookTargetValidationException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    exception.Message,
                    exception);
            }

            if (exception is CliValidationException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    exception.Message,
                    exception);
            }

            if (exception is UnsupportedUserConfigurationVersionException ||
                exception is UserConfigurationSetupInputException ||
                exception is UnsupportedNotebookFormatVersionException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    exception.Message,
                    exception);
            }

            if (exception is UserConfigurationFormatException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    exception.Message,
                    exception);
            }

            if (exception is UnsupportedWorkspaceConfigurationVersionException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The workspace configuration uses an unsupported format version. " +
                    "Run 'mn status' for details.",
                    exception);
            }

            if (exception is WorkspaceConfigurationFormatException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The workspace configuration is invalid. Run 'mn status' for details.",
                    exception);
            }

            if (exception is FileNotFoundException ||
                exception is DirectoryNotFoundException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    "A required file or directory was not found.",
                    exception);
            }

            if (exception is InvalidDataException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The input data is invalid.",
                    exception);
            }

            if (exception is NotebookTargetConflictException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Conflict,
                    exception.Message,
                    exception);
            }

            if (exception is ExternalEditorException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Storage,
                    exception.Message,
                    exception);
            }

            if (exception is UserConfigurationSaveException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Storage,
                    exception.Message,
                    exception);
            }

            if (IsStorageException(exception))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Storage,
                    "The command could not complete because of a storage error.",
                    exception);
            }

            return CliCommandResult.Failure(
                CliErrorKind.Unexpected,
                "An unexpected error occurred.",
                exception);
        }

        internal bool IsStorageException(Exception exception)
        {
            if (exception is DbException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return true;
            }

            if (exception is DbUpdateException)
                return true;

            return exception is AggregateException aggregateException &&
                aggregateException.InnerExceptions.Count > 0 &&
                aggregateException.InnerExceptions.All(IsStorageException);
        }
    }
}
