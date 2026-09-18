using System;
using System.Linq;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    /// <summary>Maps typed page operation outcomes to CLI command results.</summary>
    internal static class PageCommandResultMapper
    {
        /// <summary>Maps a page operation outcome to the common CLI result.</summary>
        /// <param name="operation">The page operation.</param>
        /// <param name="result">The typed application result.</param>
        /// <returns>The corresponding CLI result.</returns>
        internal static CliCommandResult ToCliResult(
            PageOperationKind operation,
            PageOperationResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (result.IsSuccess)
                return CliCommandResult.Success();

            var message = result.Errors.Count == 0
                ? PageOperationMessageMapper.ToFailureNotification(operation)
                : string.Join(
                    Environment.NewLine,
                    result.Errors.Select(error =>
                        PageOperationMessageMapper.ToErrorMessage(operation, error)));
            return CliCommandResult.Failure(
                ToErrorKind(result.Status),
                message);
        }

        /// <summary>Creates a page-not-found result for a target resolution failure.</summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>A not-found CLI result.</returns>
        internal static CliCommandResult NotFound(PageOperationKind operation)
        {
            return ToCliResult(
                operation,
                PageOperationResult.Failed(
                    PageOperationStatus.PageNotFound,
                    PageErrorCode.PageNotFound));
        }

        static CliErrorKind ToErrorKind(PageOperationStatus status)
        {
            return status switch
            {
                PageOperationStatus.ValidationFailed => CliErrorKind.Validation,
                PageOperationStatus.OwnerNotFound => CliErrorKind.NotFound,
                PageOperationStatus.PageNotFound => CliErrorKind.NotFound,
                PageOperationStatus.ReadOnly => CliErrorKind.Validation,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }
    }
}
