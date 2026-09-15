using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>Edits a text document with the configured external editor.</summary>
    public interface IExternalEditor
    {
        /// <summary>Opens a document and returns its edited contents.</summary>
        /// <param name="configuration">The CLI configuration used to select an editor.</param>
        /// <param name="document">The document to edit.</param>
        /// <param name="cancellationToken">Cancels the editor process.</param>
        /// <returns>The result of the editing exchange.</returns>
        Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
            ExternalEditorDocument document,
            CancellationToken cancellationToken);
    }
}
