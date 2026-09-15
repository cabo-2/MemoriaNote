using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandExecutor
    {
        readonly ICommandOutput _output;
        readonly CliErrorMapper _errorMapper;
        readonly ILogger<CliCommandExecutor> _logger;

        internal CliCommandExecutor(
            ICommandOutput output,
            CliErrorMapper errorMapper,
            ILogger<CliCommandExecutor> logger)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _errorMapper = errorMapper ??
                throw new ArgumentNullException(nameof(errorMapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal async Task<int> ExecuteAsync(
            Func<CancellationToken, Task<CliCommandResult>> body,
            CancellationToken cancellationToken)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            CliCommandResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await body(cancellationToken);
            }
            catch (Exception exception)
            {
                result = _errorMapper.Map(
                    exception,
                    cancellationToken.IsCancellationRequested);
            }

            if (result.IsSuccess)
                return (int)result.ExitCode;

            LogFailure(result);
            if (!result.AlreadyReported)
                WriteFailure(result);
            return (int)result.ExitCode;
        }

        internal Task<int> ExecuteAsync(
            Func<CancellationToken, CliCommandResult> body,
            CancellationToken cancellationToken)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            return ExecuteAsync(
                token => Task.FromResult(body(token)),
                cancellationToken);
        }

        void LogFailure(CliCommandResult result)
        {
            if (result.ErrorKind == CliErrorKind.Unexpected)
            {
                _logger.LogCritical(
                    result.Exception,
                    "A CLI command failed unexpectedly.");
                return;
            }

            if (result.ErrorKind == CliErrorKind.Storage)
            {
                _logger.LogError(
                    result.Exception,
                    "A CLI command failed due to a storage error.");
            }
            else if (result.ErrorKind == CliErrorKind.Canceled)
            {
                _logger.LogInformation("A CLI command was canceled.");
            }
            else
            {
                _logger.LogWarning(
                    "A CLI command failed: {ErrorKind}",
                    result.ErrorKind);
            }
        }

        void WriteFailure(CliCommandResult result)
        {
            var prefix = result.ErrorKind == CliErrorKind.Unexpected
                ? "Fatal"
                : "Error";
            _output.WriteErrorLine($"{prefix}: {result.Message}");
        }
    }
}
