using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Provides materialized page operations for a single notebook database.
    /// </summary>
    public interface IPageRepository
    {
        /// <summary>
        /// Reads a page by its stable identifier.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="pageId">The stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> FindPageAsync(
            string databasePath,
            Guid pageId,
            CancellationToken token);

        /// <summary>
        /// Reads a page by its typed stable identifier.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="pageId">The typed stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> FindPageAsync(
            string databasePath,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return FindPageAsync(databasePath, pageId.Value, token);
        }

        /// <summary>
        /// Reads a page from an explicitly identified notebook.
        /// </summary>
        /// <param name="notebookId">The identifier of the owning notebook.</param>
        /// <param name="pageId">The identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> FindPageAsync(
            NotebookId notebookId,
            PageId pageId,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            return FindPageAsync(notebookId.Locator, pageId, token);
        }

        /// <summary>
        /// Reads a page by its exact heading and one-based sense ordinal.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="heading">The exact page heading.</param>
        /// <param name="ordinal">The one-based sense ordinal.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The page, or null when it does not exist.</returns>
        Task<Page> FindPageAsync(
            string databasePath,
            string heading,
            int ordinal,
            CancellationToken token);

        /// <summary>
        /// Reads all pages in an exact-heading group in managed display order.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="heading">The exact page heading.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized pages.</returns>
        Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
            string databasePath,
            string heading,
            CancellationToken token);

        /// <summary>
        /// Reads an exact-heading group from an explicitly identified notebook.
        /// </summary>
        /// <param name="notebookId">The identifier of the notebook.</param>
        /// <param name="heading">The exact page heading.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized pages in managed display order.</returns>
        Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
            NotebookId notebookId,
            string heading,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            return ListPagesByHeadingAsync(notebookId.Locator, heading, token);
        }

        /// <summary>
        /// Creates a page at the end of its exact-heading group.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="heading">The page heading.</param>
        /// <param name="body">The page body.</param>
        /// <param name="directory">The optional page directory.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> CreatePageAsync(
            string databasePath,
            string heading,
            string body,
            string directory,
            CancellationToken token);

        /// <summary>
        /// Creates a page in an explicitly identified notebook.
        /// </summary>
        /// <param name="notebookId">The identifier of the target notebook.</param>
        /// <param name="heading">The page heading.</param>
        /// <param name="body">The page body.</param>
        /// <param name="directory">The optional page directory.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> CreatePageAsync(
            NotebookId notebookId,
            string heading,
            string body,
            string directory,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            return CreatePageAsync(notebookId.Locator, heading, body, directory, token);
        }

        /// <summary>
        /// Updates a page identified by its stable identifier while preserving managed ordering.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="page">The page values and stable identifier to update.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> UpdatePageAsync(
            string databasePath,
            Page page,
            CancellationToken token);

        /// <summary>
        /// Updates a page in an explicitly identified notebook.
        /// </summary>
        /// <param name="notebookId">The identifier of the owning notebook.</param>
        /// <param name="page">The replacement page values.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The persisted page.</returns>
        Task<Page> UpdatePageAsync(
            NotebookId notebookId,
            Page page,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            return UpdatePageAsync(notebookId.Locator, page, token);
        }

        /// <summary>
        /// Deletes a page by its stable identifier and compacts its exact-heading group.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="pageId">The stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>A task representing the database operation.</returns>
        Task DeletePageAsync(
            string databasePath,
            Guid pageId,
            CancellationToken token);

        /// <summary>
        /// Deletes a page by its typed stable identifier and compacts its exact-heading group.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="pageId">The typed stable identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>A task representing the database operation.</returns>
        Task DeletePageAsync(
            string databasePath,
            PageId pageId,
            CancellationToken token)
        {
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            return DeletePageAsync(databasePath, pageId.Value, token);
        }

        /// <summary>
        /// Attempts to delete a page from an explicitly identified notebook.
        /// </summary>
        /// <param name="notebookId">The identifier of the owning notebook.</param>
        /// <param name="pageId">The identifier of the page.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>True when the page was deleted; otherwise, false.</returns>
        async Task<bool> TryDeletePageAsync(
            NotebookId notebookId,
            PageId pageId,
            CancellationToken token)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (pageId == null)
                throw new ArgumentNullException(nameof(pageId));

            var page = await FindPageAsync(notebookId, pageId, token).ConfigureAwait(false);
            if (page == null)
                return false;

            await DeletePageAsync(notebookId.Locator, pageId, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Counts page summaries in a notebook.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The number of page summaries.</returns>
        Task<int> CountPagesAsync(string databasePath, CancellationToken token);

        /// <summary>
        /// Reads a page of materialized content summaries in row order.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="skipCount">The number of summaries to skip.</param>
        /// <param name="takeCount">The maximum number of summaries to return.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The materialized content summaries.</returns>
        Task<IReadOnlyList<Content>> ReadContentsAsync(
            string databasePath,
            int skipCount,
            int takeCount,
            CancellationToken token);

        /// <summary>
        /// Reads an owner-qualified page of immutable summaries in row order.
        /// </summary>
        /// <param name="databasePath">The path of the notebook database.</param>
        /// <param name="skipCount">The number of summaries to skip.</param>
        /// <param name="takeCount">The maximum number of summaries to return.</param>
        /// <param name="token">The cancellation token for the database operation.</param>
        /// <returns>The immutable, owner-qualified page summaries.</returns>
        async Task<IReadOnlyList<PageSummary>> ListPageSummariesAsync(
            string databasePath,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            var contents = await ReadContentsAsync(
                    databasePath,
                    skipCount,
                    takeCount,
                    token)
                .ConfigureAwait(false);
            var notebookId = NotebookId.FromDatabasePath(databasePath);
            var summaries = new List<PageSummary>(contents.Count);
            foreach (var content in contents)
                summaries.Add(PageSummaryMapper.FromContent(notebookId, content));

            return summaries.AsReadOnly();
        }
    }
}
