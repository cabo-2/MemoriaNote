using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli.Editors
{
    internal interface IEditorFileExchange
    {
        Task<ExternalEditorResult> EditAsync(
            ExternalEditorDocument document,
            Func<string, CancellationToken, Task> editFile,
            CancellationToken cancellationToken);
    }

    internal sealed class EditorFileExchange : IEditorFileExchange
    {
        readonly ITemporaryFileStore _temporaryFileStore;

        internal EditorFileExchange(ITemporaryFileStore temporaryFileStore)
        {
            _temporaryFileStore = temporaryFileStore ??
                throw new ArgumentNullException(nameof(temporaryFileStore));
        }

        public async Task<ExternalEditorResult> EditAsync(
            ExternalEditorDocument document,
            Func<string, CancellationToken, Task> editFile,
            CancellationToken cancellationToken)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (editFile == null)
                throw new ArgumentNullException(nameof(editFile));

            using var temporaryFile = _temporaryFileStore.CreateFile(document.FileName);
            await File.WriteAllTextAsync(
                temporaryFile.Path,
                document.Text,
                cancellationToken);
            await editFile(temporaryFile.Path, cancellationToken);

            if (!File.Exists(temporaryFile.Path))
                return ExternalEditorResult.Unchanged(document.Text);

            var editedText = await File.ReadAllTextAsync(
                temporaryFile.Path,
                Encoding.UTF8,
                cancellationToken);
            return document.Text == editedText
                ? ExternalEditorResult.Unchanged(document.Text)
                : ExternalEditorResult.Changed(editedText);
        }
    }
}
