using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using MemoriaNote.Cli.Editors;
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
                    "Operation was canceled",
                    exception);
            }

            if (exception is FileNotFoundException ||
                exception is DirectoryNotFoundException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    exception.Message,
                    exception);
            }

            if (exception is InvalidDataException ||
                exception is ConfigurationFormatException)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    exception.Message,
                    exception);
            }

            if (exception is ExternalEditorProcessException)
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
                    exception.Message,
                    exception);
            }

            return CliCommandResult.Failure(
                CliErrorKind.Unexpected,
                exception.Message,
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
