using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Coordinates owner-qualified page reads, validation, and mutations.
    /// </summary>
    public interface IPageUseCase
    {
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

        /// <summary>Creates a page after performing all required validation.</summary>
        Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token);

        /// <summary>Edits a page after performing all required validation.</summary>
        Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token);

        /// <summary>Renames a page after performing all required validation.</summary>
        Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token);

        /// <summary>Deletes a page after performing all required validation.</summary>
        Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token);
    }
}
