using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Coordinates UI-independent search, paging, page reads, and page mutations.
    /// </summary>
    public sealed class MemoriaNoteApplicationService : IMemoriaNoteApplicationService
    {
        readonly ISearchUseCase _searchUseCase;
        readonly IPageUseCase _pageUseCase;

        /// <summary>
        /// Initializes the application service from its focused use cases.
        /// </summary>
        /// <param name="searchUseCase">The search use case.</param>
        /// <param name="pageUseCase">The page use case.</param>
        public MemoriaNoteApplicationService(
            ISearchUseCase searchUseCase,
            IPageUseCase pageUseCase)
        {
            _searchUseCase = searchUseCase ??
                throw new ArgumentNullException(nameof(searchUseCase));
            _pageUseCase = pageUseCase ??
                throw new ArgumentNullException(nameof(pageUseCase));
        }

        /// <inheritdoc/>
        public Task<SearchPage> SearchAsync(
            SearchRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return _searchUseCase.SearchAsync(request, token);
        }

        /// <inheritdoc/>
        public int? GetNextPageOffset(int offset, int limit, int totalCount)
        {
            ValidatePaging(offset, limit);
            if (totalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(totalCount));
            if (limit == 0 || offset >= totalCount || limit >= totalCount - offset)
                return null;

            return offset + limit;
        }

        /// <inheritdoc/>
        public int? GetPreviousPageOffset(int offset, int limit)
        {
            ValidatePaging(offset, limit);
            if (offset == 0 || limit == 0)
                return null;

            return Math.Max(0, offset - limit);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> ReadAsync(
            PageReference target,
            CancellationToken token)
        {
            return _pageUseCase.ReadAsync(target, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> ValidateCreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.ValidateCreateAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> ValidateEditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.ValidateEditAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> ValidateRenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.ValidateRenameAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> ValidateDeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.ValidateDeleteAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.CreateAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.EditAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.RenameAsync(command, token);
        }

        /// <inheritdoc/>
        public Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            return _pageUseCase.DeleteAsync(command, token);
        }

        static void ValidatePaging(int offset, int limit)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}
