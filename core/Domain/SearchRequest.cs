using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Describes an immutable search query, its ordered notebook targets, and paging values.
    /// </summary>
    public sealed class SearchRequest
    {
        readonly IReadOnlyList<NotebookId> _notebookIds;

        SearchRequest(
            string query,
            SearchMethodType method,
            SearchRangeType scope,
            IEnumerable<NotebookId> notebookIds,
            int offset,
            int limit)
        {
            if (!Enum.IsDefined(typeof(SearchMethodType), method))
                throw new ArgumentOutOfRangeException(nameof(method));
            if (!Enum.IsDefined(typeof(SearchRangeType), scope))
                throw new ArgumentOutOfRangeException(nameof(scope));
            if (notebookIds == null)
                throw new ArgumentNullException(nameof(notebookIds));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit));

            var copiedNotebookIds = new List<NotebookId>(notebookIds);
            if (copiedNotebookIds.Exists(notebookId => notebookId == null))
                throw new ArgumentException("Search targets cannot contain null.", nameof(notebookIds));
            if (scope == SearchRangeType.Notebook && copiedNotebookIds.Count > 1)
            {
                throw new ArgumentException(
                    "A note-scoped search can target at most one note.",
                    nameof(notebookIds));
            }

            Query = query ?? string.Empty;
            Method = method;
            Scope = scope;
            _notebookIds = new ReadOnlyCollection<NotebookId>(copiedNotebookIds);
            Offset = offset;
            Limit = limit;
        }

        /// <summary>
        /// Creates a request that searches one notebook, or none when there is no selected target.
        /// </summary>
        /// <param name="query">The search text. Null is treated as an empty query.</param>
        /// <param name="method">The matching method.</param>
        /// <param name="notebookId">The target notebook, or null when none is selected.</param>
        /// <param name="offset">The zero-based result offset.</param>
        /// <param name="limit">The maximum number of results to return.</param>
        /// <returns>An immutable notebook-scoped request.</returns>
        public static SearchRequest ForNotebook(
            string query,
            SearchMethodType method,
            NotebookId notebookId,
            int offset,
            int limit)
        {
            var notebookIds = notebookId == null
                ? Array.Empty<NotebookId>()
                : new[] { notebookId };
            return new SearchRequest(
                query,
                method,
                SearchRangeType.Notebook,
                notebookIds,
                offset,
                limit);
        }

        /// <summary>
        /// Creates a request that searches an ordered collection of notebooks.
        /// </summary>
        /// <param name="query">The search text. Null is treated as an empty query.</param>
        /// <param name="method">The matching method.</param>
        /// <param name="notebookIds">The notebook identifiers in search priority order.</param>
        /// <param name="offset">The zero-based result offset.</param>
        /// <param name="limit">The maximum number of results to return.</param>
        /// <returns>An immutable workspace-scoped request.</returns>
        public static SearchRequest ForWorkspace(
            string query,
            SearchMethodType method,
            IEnumerable<NotebookId> notebookIds,
            int offset,
            int limit)
        {
            return new SearchRequest(
                query,
                method,
                SearchRangeType.Workspace,
                notebookIds,
                offset,
                limit);
        }

        /// <summary>
        /// Gets the search text.
        /// </summary>
        public string Query { get; }

        /// <summary>
        /// Gets the search matching method.
        /// </summary>
        public SearchMethodType Method { get; }

        /// <summary>
        /// Gets the search scope.
        /// </summary>
        public SearchRangeType Scope { get; }

        /// <summary>
        /// Gets the search targets in priority order.
        /// </summary>
        public IReadOnlyList<NotebookId> NotebookIds => _notebookIds;

        /// <summary>
        /// Gets the zero-based result offset.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// Gets the maximum number of results to return.
        /// </summary>
        public int Limit { get; }
    }
}
