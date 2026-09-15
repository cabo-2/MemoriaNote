using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli.Terminal
{
    internal sealed class TerminalUiAdapter : ITerminalUi
    {
        readonly IPageEditorWorkflow _pageEditorWorkflow;
        readonly ILoggerFactory _loggerFactory;

        internal TerminalUiAdapter(
            IPageEditorWorkflow pageEditorWorkflow,
            ILoggerFactory loggerFactory)
        {
            _pageEditorWorkflow = pageEditorWorkflow ??
                throw new ArgumentNullException(nameof(pageEditorWorkflow));
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
            return new ScreenController(_pageEditorWorkflow, _loggerFactory);
        }
    }
}
