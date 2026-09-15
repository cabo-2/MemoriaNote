using System;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    internal sealed class CliCommandExecutor
    {
        readonly ICommandOutput _output;
        readonly ILogger<CliCommandExecutor> _logger;

        internal CliCommandExecutor(
            ICommandOutput output,
            ILogger<CliCommandExecutor> logger)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal int Execute(Func<int> body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            try
            {
                return body();
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "A CLI command failed unexpectedly.");
                _output.WriteErrorLine($"Fatal: {exception.Message}");
                return -1;
            }
        }
    }
}
