using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Application
{
    /// <summary>
    /// Creates a live notebook without changing workspace or user configuration.
    /// </summary>
    public sealed class CreateNotebookUseCase
    {
        readonly INotebookMigrator _notebookMigrator;

        /// <summary>
        /// Initializes a notebook creation use case.
        /// </summary>
        /// <param name="notebookMigrator">The persistence boundary used to create the notebook.</param>
        public CreateNotebookUseCase(INotebookMigrator notebookMigrator)
        {
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
        }

        /// <summary>
        /// Creates a notebook using the current storage format.
        /// </summary>
        /// <param name="name">The notebook name.</param>
        /// <param name="title">The notebook title.</param>
        /// <param name="notebookPath">The output file path.</param>
        /// <param name="cancellationToken">The cancellation token for the operation.</param>
        /// <returns>The newly created notebook.</returns>
        public Task<Notebook> CreateAsync(
            string name,
            string title,
            string notebookPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A notebook name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("A notebook title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(notebookPath))
                throw new ArgumentException("A notebook path is required.", nameof(notebookPath));

            return _notebookMigrator.CreateAsync(
                name,
                title,
                notebookPath,
                cancellationToken);
        }
    }
}
