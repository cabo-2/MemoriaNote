using System;

namespace MemoriaNote.Domain
{
    /// <summary>
    /// Describes a read-only page listing for one explicitly identified notebook.
    /// </summary>
    public sealed class PageListRequest
    {
        /// <summary>
        /// Initializes a page listing request.
        /// </summary>
        /// <param name="notebookId">The notebook whose pages will be listed.</param>
        /// <param name="limit">The optional maximum number of pages to return.</param>
        public PageListRequest(NotebookId notebookId, int? limit = null)
        {
            if (limit <= 0)
                throw new ArgumentOutOfRangeException(nameof(limit));

            NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
            Limit = limit;
        }

        /// <summary>Gets the notebook whose pages will be listed.</summary>
        public NotebookId NotebookId { get; }

        /// <summary>Gets the optional maximum number of pages to return.</summary>
        public int? Limit { get; }
    }
}
