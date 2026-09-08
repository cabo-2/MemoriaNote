using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Provides materialized page operations for a single note database.
    /// </summary>
    public interface INoteRepository
    {
        /// <summary>
        /// Reads a page by its stable identifier.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="pageId">The stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> ReadPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token);

        /// <summary>
        /// Reads a page by its exact name and one-based sense index.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="name">The exact page name.</param>
        /// <param name="index">The one-based sense index.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> ReadPageAsync(
            string dataSource,
            string name,
            int index,
            CancellationToken token);

        /// <summary>
        /// Reads all pages in an exact-name group in managed display order.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="name">The exact page name.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized pages.</returns>
        Task<IReadOnlyList<Page>> ReadPagesAsync(
            string dataSource,
            string name,
            CancellationToken token);

        /// <summary>
        /// Creates a page at the end of its exact-name group.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="name">The page name.</param>
        /// <param name="text">The page text.</param>
        /// <param name="directory">The optional page directory.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> CreatePageAsync(
            string dataSource,
            string name,
            string text,
            string directory,
            CancellationToken token);

        /// <summary>
        /// Updates a page identified by its stable identifier while preserving managed ordering.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="page">The page values and stable identifier to update.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> UpdatePageAsync(
            string dataSource,
            Page page,
            CancellationToken token);

        /// <summary>
        /// Deletes a page by its stable identifier and compacts its exact-name group.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="pageId">The stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>A task representing the database operation.</returns>
        Task DeletePageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token);

        /// <summary>
        /// Counts page summaries in a note.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The number of page summaries.</returns>
        Task<int> CountAsync(string dataSource, CancellationToken token);

        /// <summary>
        /// Reads a page of materialized content summaries in row order.
        /// </summary>
        /// <param name="dataSource">The path of the note database.</param>
        /// <param name="skipCount">The number of summaries to skip.</param>
        /// <param name="takeCount">The maximum number of summaries to return.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized content summaries.</returns>
        Task<IReadOnlyList<Content>> ReadContentsAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token);
    }
}
