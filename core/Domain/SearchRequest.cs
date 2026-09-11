using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Describes an immutable search query, its ordered note targets, and paging values.
    /// </summary>
    public sealed class SearchRequest
    {
        readonly IReadOnlyList<NoteId> _noteIds;

        SearchRequest(
            string query,
            SearchMethodType method,
            SearchRangeType scope,
            IEnumerable<NoteId> noteIds,
            int offset,
            int limit)
        {
            if (!Enum.IsDefined(typeof(SearchMethodType), method))
                throw new ArgumentOutOfRangeException(nameof(method));
            if (!Enum.IsDefined(typeof(SearchRangeType), scope))
                throw new ArgumentOutOfRangeException(nameof(scope));
            if (noteIds == null)
                throw new ArgumentNullException(nameof(noteIds));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit));

            var copiedNoteIds = new List<NoteId>(noteIds);
            if (copiedNoteIds.Exists(noteId => noteId == null))
                throw new ArgumentException("Search targets cannot contain null.", nameof(noteIds));
            if (scope == SearchRangeType.Note && copiedNoteIds.Count > 1)
            {
                throw new ArgumentException(
                    "A note-scoped search can target at most one note.",
                    nameof(noteIds));
            }

            Query = query ?? string.Empty;
            Method = method;
            Scope = scope;
            _noteIds = new ReadOnlyCollection<NoteId>(copiedNoteIds);
            Offset = offset;
            Limit = limit;
        }

        /// <summary>
        /// Creates a request that searches one note, or no note when there is no selected target.
        /// </summary>
        /// <param name="query">The search text. Null is treated as an empty query.</param>
        /// <param name="method">The matching method.</param>
        /// <param name="noteId">The target note, or null when no note is selected.</param>
        /// <param name="offset">The zero-based result offset.</param>
        /// <param name="limit">The maximum number of results to return.</param>
        /// <returns>An immutable note-scoped request.</returns>
        public static SearchRequest ForNote(
            string query,
            SearchMethodType method,
            NoteId noteId,
            int offset,
            int limit)
        {
            var noteIds = noteId == null
                ? Array.Empty<NoteId>()
                : new[] { noteId };
            return new SearchRequest(
                query,
                method,
                SearchRangeType.Note,
                noteIds,
                offset,
                limit);
        }

        /// <summary>
        /// Creates a request that searches an ordered collection of notes.
        /// </summary>
        /// <param name="query">The search text. Null is treated as an empty query.</param>
        /// <param name="method">The matching method.</param>
        /// <param name="noteIds">The note identifiers in search priority order.</param>
        /// <param name="offset">The zero-based result offset.</param>
        /// <param name="limit">The maximum number of results to return.</param>
        /// <returns>An immutable workgroup-scoped request.</returns>
        public static SearchRequest ForWorkgroup(
            string query,
            SearchMethodType method,
            IEnumerable<NoteId> noteIds,
            int offset,
            int limit)
        {
            return new SearchRequest(
                query,
                method,
                SearchRangeType.Workgroup,
                noteIds,
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
        public IReadOnlyList<NoteId> NoteIds => _noteIds;

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
