using System;
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

        public void RunHome(MemoriaNoteViewModel viewModel)
        {
            var controller = CreateController();
            controller.RequestHome();
            controller.Start(viewModel);
        }

        public void RunManage(MemoriaNoteViewModel viewModel, bool openEditor)
        {
            var controller = CreateController();
            controller.RequestManage();
            if (openEditor)
                controller.RequestEditor();
            controller.Start(viewModel);
        }

        ScreenController CreateController()
        {
            return new ScreenController(_terminalEditorFactory, _loggerFactory);
        }
    }
}
