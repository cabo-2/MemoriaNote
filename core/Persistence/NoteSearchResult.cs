using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Contains typed page summaries returned by a single-note repository search.
    /// </summary>
    public sealed class NoteSearchResult
    {
        readonly IReadOnlyList<PageSummary> _pageSummaries;

        /// <summary>
        /// Initializes a repository search result.
        /// </summary>
        /// <param name="pageSummaries">The ordered, paged summaries.</param>
        /// <param name="count">The unpaged total result count.</param>
        /// <param name="startTime">The search start time.</param>
        /// <param name="endTime">The search end time.</param>
        public NoteSearchResult(
            IEnumerable<PageSummary> pageSummaries,
            int count,
            DateTime startTime,
            DateTime endTime)
        {
            if (pageSummaries == null)
                throw new ArgumentNullException(nameof(pageSummaries));

            _pageSummaries = new ReadOnlyCollection<PageSummary>(
                new List<PageSummary>(pageSummaries));
            Count = count;
            StartTime = startTime;
            EndTime = endTime;
        }

        /// <summary>
        /// Gets the ordered, paged summaries.
        /// </summary>
        public IReadOnlyList<PageSummary> PageSummaries => _pageSummaries;

        /// <summary>
        /// Gets the unpaged total result count.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets the search start time.
        /// </summary>
        public DateTime StartTime { get; }

        /// <summary>
        /// Gets the search end time.
        /// </summary>
        public DateTime EndTime { get; }
    }
}
