using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Transfer
{
    /// <summary>
    /// Exports notebook pages as text files.
    /// </summary>
    public sealed class TextPageExporter
    {
        readonly INotebookTransferRepository _transferRepository;
        readonly PageFileNameCodec _fileNameCodec;
        readonly TextTransferPathCodec _pathCodec;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextPageExporter"/> class.
        /// </summary>
        /// <param name="transferRepository">The repository used to read exported pages.</param>
        public TextPageExporter(INotebookTransferRepository transferRepository)
            : this(transferRepository, new PageFileNameCodec())
        {
        }

        /// <summary>
        /// Initializes a text page exporter with an explicit file-name codec.
        /// </summary>
        /// <param name="transferRepository">The repository used to read exported pages.</param>
        /// <param name="fileNameCodec">The codec used for file and directory names.</param>
        public TextPageExporter(
            INotebookTransferRepository transferRepository,
            PageFileNameCodec fileNameCodec)
        {
            _transferRepository = transferRepository ??
                throw new ArgumentNullException(nameof(transferRepository));
            _fileNameCodec = fileNameCodec ??
                throw new ArgumentNullException(nameof(fileNameCodec));
            _pathCodec = new TextTransferPathCodec(_fileNameCodec);
        }

        /// <summary>
        /// Exports every page from a notebook to a directory.
        /// </summary>
        /// <param name="notebookId">The source notebook.</param>
        /// <param name="exportDirectory">The destination directory.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A task representing the export operation.</returns>
        public async Task ExportAsync(
            NotebookId notebookId,
            string exportDirectory,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (exportDirectory == null)
                throw new ArgumentNullException(nameof(exportDirectory));

            token.ThrowIfCancellationRequested();
            var pages = await _transferRepository.ListPagesAsync(notebookId, token)
                .ConfigureAwait(false);
            foreach (var page in pages)
            {
                token.ThrowIfCancellationRequested();
                var directory = exportDirectory;
                if (page.TagDict.TryGetValue(PageTag.Dir, out var relativeDirectory))
                {
                    directory = Path.Combine(
                        exportDirectory,
                        _pathCodec.EncodeRelativePath(relativeDirectory));
                    Directory.CreateDirectory(directory);
                }

                var fileName = _fileNameCodec.Encode(page.Name) + ".txt";
                var path = Path.Combine(directory, fileName);
                await File.WriteAllTextAsync(path, page.Text ?? string.Empty, token)
                    .ConfigureAwait(false);
            }
        }
    }
}
