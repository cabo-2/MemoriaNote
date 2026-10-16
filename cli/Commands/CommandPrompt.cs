using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli
{
    internal sealed class CommandPrompt
    {
        readonly ICommandInput _input;
        readonly ICommandOutput _output;
        readonly bool _isOutputInteractive;

        internal CommandPrompt(
            ICommandInput input,
            ICommandOutput output,
            bool isOutputInteractive)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _isOutputInteractive = isOutputInteractive;
        }

        internal bool IsInteractive => _input.IsInteractive && _isOutputInteractive;

        internal async Task<bool> ConfirmPageDeletionAsync(
            string pageName,
            string pageId,
            CancellationToken cancellationToken)
        {
            _output.WriteError($"Delete page '{pageName}' ({pageId})? [y/N] ");
            var response = await _input.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
