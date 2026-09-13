using System;

namespace MemoriaNote
{
    /// <summary>
    /// Provides the current metadata snapshot and persistence ports for one notebook.
    /// </summary>
    public sealed class NotebookContext
    {
        /// <summary>
        /// Initializes a resolved notebook context.
        /// </summary>
        /// <param name="notebookId">The resolved notebook identifier.</param>
        /// <param name="isReadOnly">Whether the current metadata snapshot is read-only.</param>
        /// <param name="pageRepository">The page repository.</param>
        /// <param name="searchRepository">The optional search repository.</param>
        public NotebookContext(
            NotebookId notebookId,
            bool isReadOnly,
            IPageRepository pageRepository,
            IPageSearchRepository searchRepository = null)
        {
            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            IsReadOnly = isReadOnly;
            PageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
            SearchRepository = searchRepository;
        }

        /// <summary>
        /// Gets the resolved notebook identifier.
        /// </summary>
        public NotebookId NotebookId { get; }

        /// <summary>
        /// Gets whether the current metadata snapshot is read-only.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        /// Gets the page persistence port.
        /// </summary>
        public IPageRepository PageRepository { get; }

        /// <summary>
        /// Gets the optional search persistence port.
        /// </summary>
        public IPageSearchRepository SearchRepository { get; }
    }

    /// <summary>
    /// Resolves a typed notebook identifier against the current notebook collection.
    /// </summary>
    public interface INotebookContextResolver
    {
        /// <summary>
        /// Resolves the current notebook context.
        /// </summary>
        /// <param name="notebookId">The notebook identifier.</param>
        /// <returns>The current context, or null when the notebook is not available.</returns>
        NotebookContext Resolve(NotebookId notebookId);
    }
}
