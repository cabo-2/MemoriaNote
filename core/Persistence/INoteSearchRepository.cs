using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Provides materialized search results for a single note database.
    /// </summary>
    public interface INoteSearchRepository
    {
        /// <summary>
        /// Searches a note and returns an ordered page together with the unpaged total count.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="skipCount">The number of matches to skip.</param>
        /// <param name="takeCount">The maximum number of matches to return.</param>
        /// <param name="token">The cancellation token for database operations.</param>
        /// <returns>The ordered search results and total count.</returns>
        Task<SearchResult> SearchAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token);

        /// <summary>
        /// Counts every match in a note without applying paging.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The number of matching contents.</returns>
        Task<int> CountAsync(
            string dataSource,
            string searchEntry,
            SearchMethodType searchMethod,
            CancellationToken token);
    }
}
