using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Exposes UI-independent search and page workflows to presentation adapters.
    /// </summary>
    public interface IMemoriaNoteApplicationService
    {
        /// <summary>Executes an immutable search request.</summary>
        Task<SearchPage> SearchAsync(SearchRequest request, CancellationToken token);

        /// <summary>Calculates the next page offset, or null when already at the end.</summary>
        int? GetNextPageOffset(int offset, int limit, int totalCount);

        /// <summary>Calculates the previous page offset, or null when already at the beginning.</summary>
        int? GetPreviousPageOffset(int offset, int limit);

        /// <summary>Reads an owner-qualified page.</summary>
        Task<PageOperationResult> ReadAsync(PageReference target, CancellationToken token);

        /// <summary>Validates a page creation request.</summary>
        Task<PageOperationResult> ValidateCreateAsync(
            CreatePageCommand command,
            CancellationToken token);

        /// <summary>Validates a page edit request.</summary>
        Task<PageOperationResult> ValidateEditAsync(
            EditPageCommand command,
            CancellationToken token);

        /// <summary>Validates a page rename request.</summary>
        Task<PageOperationResult> ValidateRenameAsync(
            RenamePageCommand command,
            CancellationToken token);

        /// <summary>Validates a page deletion request.</summary>
        Task<PageOperationResult> ValidateDeleteAsync(
            DeletePageCommand command,
            CancellationToken token);

        /// <summary>Creates a page.</summary>
        Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token);

        /// <summary>Edits a page.</summary>
        Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token);

        /// <summary>Renames a page.</summary>
        Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token);

        /// <summary>Deletes a page.</summary>
        Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token);
    }
}
