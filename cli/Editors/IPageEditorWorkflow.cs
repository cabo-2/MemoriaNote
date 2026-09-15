using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>Runs the external editor workflow for the selected page operation.</summary>
    public interface IPageEditorWorkflow
    {
        /// <summary>Runs the workflow selected by the view model editing state.</summary>
        /// <param name="viewModel">The presentation state to edit.</param>
        /// <param name="cancellationToken">Cancels the editing workflow.</param>
        /// <returns>A task representing the workflow.</returns>
        Task RunAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken);
    }
}
