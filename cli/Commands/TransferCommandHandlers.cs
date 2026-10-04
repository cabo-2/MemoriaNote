using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal sealed class ImportCommandHandler
    {
        readonly CliCommandExecutor _executor;

        internal ImportCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(
            string importDirectory,
            bool recursive,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The 'mn import' command is no longer supported. " +
                    "Use 'mn notebooks import <directory> " +
                    "[--notebook <notebook>] [--on-conflict fail|skip|replace] " +
                    "[--dry-run]' instead."),
                cancellationToken);
        }
    }

    internal sealed class ExportCommandHandler
    {
        readonly CliCommandExecutor _executor;

        internal ExportCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(
            string exportDirectory,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The 'mn export' command is no longer supported. " +
                    "Use 'mn notebooks export <directory> " +
                    "[--notebook <notebook>] " +
                    "[--name-conflict fail|id-suffix]' instead."),
                cancellationToken);
        }
    }
}
