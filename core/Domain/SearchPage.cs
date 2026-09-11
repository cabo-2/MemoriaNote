using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Contains an immutable ordered slice of search results and its unpaged total count.
    /// </summary>
    public sealed class SearchPage
    {
        readonly IReadOnlyList<PageSummary> _items;

        /// <summary>
        /// Initializes a search result page.
        /// </summary>
        /// <param name="items">The ordered result slice.</param>
        /// <param name="totalCount">The number of matches before paging.</param>
        /// <param name="offset">The requested zero-based offset.</param>
        /// <param name="limit">The requested maximum result count.</param>
        public SearchPage(
            IEnumerable<PageSummary> items,
            int totalCount,
            int offset,
            int limit)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (totalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(totalCount));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit));

            var copiedItems = new List<PageSummary>(items);
            if (copiedItems.Exists(item => item == null))
                throw new ArgumentException("Search results cannot contain null.", nameof(items));

            _items = new ReadOnlyCollection<PageSummary>(copiedItems);
            TotalCount = totalCount;
            Offset = offset;
            Limit = limit;
        }

        /// <summary>
        /// Gets the ordered result slice.
        /// </summary>
        public IReadOnlyList<PageSummary> Items => _items;

        /// <summary>
        /// Gets the number of matches before paging.
        /// </summary>
        public int TotalCount { get; }

        /// <summary>
        /// Gets the requested zero-based offset.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// Gets the requested maximum result count.
        /// </summary>
        public int Limit { get; }
    }
}
