using System;

namespace MemoriaNote.Cli
{
    internal enum CliExitCode
    {
        Success = 0,
        Unexpected = 1,
        Validation = 2,
        NotFound = 3,
        Conflict = 4,
        Storage = 5,
        Canceled = 130
    }

    internal enum CliErrorKind
    {
        Validation,
        NotFound,
        Conflict,
        Storage,
        Canceled,
        Unexpected
    }

    internal sealed class CliCommandResult
    {
        CliCommandResult(
            CliExitCode exitCode,
            CliErrorKind? errorKind,
            string message,
            Exception exception,
            bool alreadyReported)
        {
            ExitCode = exitCode;
            ErrorKind = errorKind;
            Message = message;
            Exception = exception;
            AlreadyReported = alreadyReported;
        }

        internal CliExitCode ExitCode { get; }

        internal CliErrorKind? ErrorKind { get; }

        internal string Message { get; }

        internal Exception Exception { get; }

        internal bool AlreadyReported { get; }

        internal bool IsSuccess => ExitCode == CliExitCode.Success;

        internal static CliCommandResult Success()
        {
            return new CliCommandResult(
                CliExitCode.Success,
                null,
                null,
                null,
                false);
        }

        internal static CliCommandResult Failure(
            CliErrorKind errorKind,
            string message,
            Exception exception = null,
            bool alreadyReported = false)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("An error message is required.", nameof(message));

            return new CliCommandResult(
                ToExitCode(errorKind),
                errorKind,
                message,
                exception,
                alreadyReported);
        }

        static CliExitCode ToExitCode(CliErrorKind errorKind)
        {
            return errorKind switch
            {
                CliErrorKind.Validation => CliExitCode.Validation,
                CliErrorKind.NotFound => CliExitCode.NotFound,
                CliErrorKind.Conflict => CliExitCode.Conflict,
                CliErrorKind.Storage => CliExitCode.Storage,
                CliErrorKind.Canceled => CliExitCode.Canceled,
                CliErrorKind.Unexpected => CliExitCode.Unexpected,
                _ => throw new ArgumentOutOfRangeException(nameof(errorKind))
            };
        }
    }
}
