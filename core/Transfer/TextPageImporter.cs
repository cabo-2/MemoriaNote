using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Imports text files as pages in a notebook.
    /// </summary>
    public sealed class TextPageImporter
    {
        readonly IPageRepository _pageRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextPageImporter"/> class.
        /// </summary>
        /// <param name="pageRepository">The repository used to create imported pages.</param>
        public TextPageImporter(IPageRepository pageRepository)
        {
            _pageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
        }

        /// <summary>
        /// Imports text files from a directory.
        /// </summary>
        /// <param name="notebookId">The target notebook.</param>
        /// <param name="importDirectory">The source directory.</param>
        /// <param name="recursive">Whether child directories should be imported.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>A task representing the import operation.</returns>
        public async Task ImportAsync(
            NotebookId notebookId,
            string importDirectory,
            bool recursive,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (importDirectory == null)
                throw new ArgumentNullException(nameof(importDirectory));

            token.ThrowIfCancellationRequested();
            foreach (var source in EnumerateTextFiles(importDirectory, recursive, null))
            {
                token.ThrowIfCancellationRequested();
                var text = await File.ReadAllTextAsync(source.File.FullName, token)
                    .ConfigureAwait(false);
                var name = TextUtil.ReplaceNameStringReverse(
                    Path.GetFileNameWithoutExtension(source.File.Name));
                await _pageRepository.CreatePageAsync(
                        notebookId,
                        name,
                        text,
                        TextUtil.ConvertGenericPath(source.RelativeDirectory),
                        token)
                    .ConfigureAwait(false);
            }
        }

        static IEnumerable<ImportSource> EnumerateTextFiles(
            string importDirectory,
            bool recursive,
            string relativeDirectory)
        {
            var targetDirectory = relativeDirectory == null
                ? importDirectory
                : Path.Combine(importDirectory, relativeDirectory);
            var directory = new DirectoryInfo(targetDirectory);
            foreach (var file in directory.GetFiles("*.txt"))
                yield return new ImportSource(file, relativeDirectory);

            if (!recursive)
                yield break;

            foreach (var child in directory.EnumerateDirectories()
                .Where(candidate => candidate.Name.FirstOrDefault() != '.'))
            {
                var childRelativePath = Path.GetRelativePath(
                    importDirectory,
                    child.FullName);
                foreach (var source in EnumerateTextFiles(
                    importDirectory,
                    recursive,
                    childRelativePath))
                {
                    yield return source;
                }
            }
        }

        sealed class ImportSource
        {
            internal ImportSource(FileInfo file, string relativeDirectory)
            {
                File = file;
                RelativeDirectory = relativeDirectory;
            }

            internal FileInfo File { get; }

            internal string RelativeDirectory { get; }
        }
    }
}
