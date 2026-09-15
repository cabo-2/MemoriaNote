using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli.Terminal
{
    /// <summary>Adapts the page editor workflow to the terminal screen stack.</summary>
    public sealed class EditorView : ITerminalScreen
    {
        readonly IPageEditorWorkflow _pageEditorWorkflow;

        /// <summary>Initializes an editor screen adapter.</summary>
        /// <param name="controller">The screen controller.</param>
        /// <param name="viewModel">The presentation state.</param>
        /// <param name="pageEditorWorkflow">The external page editor workflow.</param>
        public EditorView(
            ScreenController controller,
            MemoriaNoteViewModel viewModel,
            IPageEditorWorkflow pageEditorWorkflow)
        {
            Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _pageEditorWorkflow = pageEditorWorkflow ??
                throw new ArgumentNullException(nameof(pageEditorWorkflow));
        }

        /// <summary>Runs the page editor workflow for the screen.</summary>
        /// <param name="controller">The screen controller.</param>
        /// <param name="viewModel">The presentation state.</param>
        /// <param name="pageEditorWorkflow">The external page editor workflow.</param>
        /// <param name="cancellationToken">Cancels the editing workflow.</param>
        /// <returns>A task representing the editing workflow.</returns>
        public static Task RunAsync(
            ScreenController controller,
            MemoriaNoteViewModel viewModel,
            IPageEditorWorkflow pageEditorWorkflow,
            CancellationToken cancellationToken)
        {
            return new EditorView(controller, viewModel, pageEditorWorkflow)
                .StartAsync(cancellationToken);
        }

        /// <summary>Gets or sets the screen controller.</summary>
        public ScreenController Controller { get; set; }

        /// <summary>Gets or sets the presentation state.</summary>
        public MemoriaNoteViewModel ViewModel { get; set; }

        Task StartAsync(CancellationToken cancellationToken)
        {
            return _pageEditorWorkflow.RunAsync(ViewModel, cancellationToken);
        }
    }
}
