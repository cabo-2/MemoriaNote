using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Provides owner-qualified page search results for notebook databases.
    /// </summary>
    public interface IPageSearchRepository
    {
        /// <summary>
        /// Searches one notebook and returns an ordered slice of immutable page summaries.
        /// </summary>
        /// <param name="notebookId">The identifier of the notebook database.</param>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="skipCount">The number of matches to skip.</param>
        /// <param name="takeCount">The maximum number of matches to return.</param>
        /// <param name="token">The cancellation token for database operations.</param>
        /// <returns>The typed, ordered search results.</returns>
        Task<IReadOnlyList<PageSummary>> SearchAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token);

        /// <summary>
        /// Counts every match in a notebook without applying paging.
        /// </summary>
        /// <param name="notebookId">The identifier of the notebook database.</param>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The number of matching pages.</returns>
        Task<int> CountMatchesAsync(
            NotebookId notebookId,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token);
    }
}
