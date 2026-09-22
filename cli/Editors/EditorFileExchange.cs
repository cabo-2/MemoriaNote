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
        static readonly Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

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

            using var temporaryFile = _temporaryFileStore.CreateFile(
                document.FileName + ".txt");
            try
            {
                await File.WriteAllTextAsync(
                    temporaryFile.Path,
                    document.Text,
                    StrictUtf8,
                    cancellationToken);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ExternalEditorExchangeException(
                    "The stored document cannot be encoded as valid UTF-8.",
                    exception);
            }
            await editFile(temporaryFile.Path, cancellationToken);

            if (!File.Exists(temporaryFile.Path))
            {
                throw new ExternalEditorExchangeException(
                    "The external editor removed the temporary document.");
            }

            string editedText;
            try
            {
                editedText = await File.ReadAllTextAsync(
                    temporaryFile.Path,
                    StrictUtf8,
                    cancellationToken);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ExternalEditorExchangeException(
                    "The external editor produced a document that is not valid UTF-8.",
                    exception);
            }
            return document.Text == editedText
                ? ExternalEditorResult.Unchanged(document.Text)
                : ExternalEditorResult.Changed(editedText);
        }
    }
}
