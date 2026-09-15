using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    internal sealed class TerminalUiAdapter : ITerminalUi
    {
        readonly TerminalEditorFactory _terminalEditorFactory;
        readonly ILoggerFactory _loggerFactory;

        internal TerminalUiAdapter(
            TerminalEditorFactory terminalEditorFactory,
            ILoggerFactory loggerFactory)
        {
            _terminalEditorFactory = terminalEditorFactory ??
                throw new ArgumentNullException(nameof(terminalEditorFactory));
            _loggerFactory = loggerFactory ??
                throw new ArgumentNullException(nameof(loggerFactory));
        }

        public Task RunHomeAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            var controller = CreateController();
            controller.RequestHome();
            return controller.StartAsync(viewModel, cancellationToken);
        }

        public Task RunManageAsync(
            MemoriaNoteViewModel viewModel,
            bool openEditor,
            CancellationToken cancellationToken)
        {
            var controller = CreateController();
            controller.RequestManage();
            if (openEditor)
                controller.RequestEditor();
            return controller.StartAsync(viewModel, cancellationToken);
        }

        ScreenController CreateController()
        {
            return new ScreenController(_terminalEditorFactory, _loggerFactory);
        }
    }
}
