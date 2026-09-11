using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Coordinates owner-qualified page reads, validation, and mutations.
    /// </summary>
    public sealed class PageUseCase : IPageUseCase
    {
        readonly INoteContextResolver _contextResolver;
        readonly PageValidationPolicy _validationPolicy;

        /// <summary>
        /// Initializes a page use case.
        /// </summary>
        /// <param name="contextResolver">The current note context resolver.</param>
        /// <param name="validationPolicy">The I/O-free page validation policy.</param>
        public PageUseCase(
            INoteContextResolver contextResolver,
            PageValidationPolicy validationPolicy)
        {
            _contextResolver = contextResolver ??
                throw new ArgumentNullException(nameof(contextResolver));
            _validationPolicy = validationPolicy ??
                throw new ArgumentNullException(nameof(validationPolicy));
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> ReadAsync(
            PageReference target,
            CancellationToken token)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            token.ThrowIfCancellationRequested();
            var context = _contextResolver.Resolve(target.NoteId);
            if (context == null)
                return OwnerNotFound();

            var page = await context.PageRepository
                .ReadPageAsync(target.NoteId, target.PageId, token)
                .ConfigureAwait(false);
            return page == null
                ? PageNotFound()
                : PageOperationResult.Succeeded(page);
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> ValidateCreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            return (await ValidateCreateCoreAsync(command, token).ConfigureAwait(false)).Result;
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> ValidateEditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            return (await ValidateEditCoreAsync(command, token).ConfigureAwait(false)).Result;
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> ValidateRenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            return (await ValidateRenameCoreAsync(command, token).ConfigureAwait(false)).Result;
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> ValidateDeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            return (await ValidateDeleteCoreAsync(command, token).ConfigureAwait(false)).Result;
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            var validation = await ValidateCreateCoreAsync(command, token).ConfigureAwait(false);
            if (!validation.Result.IsSuccess)
                return validation.Result;

            var page = await validation.Context.PageRepository
                .CreatePageAsync(
                    command.NoteId,
                    command.Name,
                    command.Text,
                    command.Directory,
                    token)
                .ConfigureAwait(false);
            return PageOperationResult.Succeeded(page);
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            var validation = await ValidateEditCoreAsync(command, token).ConfigureAwait(false);
            if (!validation.Result.IsSuccess)
                return validation.Result;

            validation.Page.Text = command.Text;
            try
            {
                var page = await validation.Context.PageRepository
                    .UpdatePageAsync(command.NoteId, validation.Page, token)
                    .ConfigureAwait(false);
                return PageOperationResult.Succeeded(page);
            }
            catch (KeyNotFoundException)
            {
                return PageNotFound();
            }
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            var validation = await ValidateRenameCoreAsync(command, token).ConfigureAwait(false);
            if (!validation.Result.IsSuccess)
                return validation.Result;

            validation.Page.Name = command.Name;
            try
            {
                var page = await validation.Context.PageRepository
                    .UpdatePageAsync(command.NoteId, validation.Page, token)
                    .ConfigureAwait(false);
                return PageOperationResult.Succeeded(page);
            }
            catch (KeyNotFoundException)
            {
                return PageNotFound();
            }
        }

        /// <inheritdoc/>
        public async Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            var validation = await ValidateDeleteCoreAsync(command, token).ConfigureAwait(false);
            if (!validation.Result.IsSuccess)
                return validation.Result;

            var deleted = await validation.Context.PageRepository
                .TryDeletePageAsync(command.NoteId, command.PageId, token)
                .ConfigureAwait(false);
            return deleted
                ? PageOperationResult.Succeeded()
                : PageNotFound();
        }

        async Task<ValidatedOperation> ValidateCreateCoreAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var contextResult = ResolveWritableContext(command.NoteId, token);
            if (!contextResult.Result.IsSuccess)
                return contextResult;

            var errors = _validationPolicy.ValidateName(command.Name).ToList();
            errors.AddRange(_validationPolicy.ValidateText(command.Text));
            if (errors.Count > 0)
                return ValidatedOperation.Failed(PageOperationResult.ValidationFailed(errors));

            var duplicates = await contextResult.Context.PageRepository
                .ReadPagesAsync(command.NoteId, command.Name, token)
                .ConfigureAwait(false);
            if (duplicates.Count > 0)
            {
                return ValidatedOperation.Failed(
                    PageOperationResult.ValidationFailed(
                        new[] { PageErrorCode.DuplicateName }));
            }

            return contextResult;
        }

        async Task<ValidatedOperation> ValidateEditCoreAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var targetResult = await ResolveWritablePageAsync(command.Target, token)
                .ConfigureAwait(false);
            if (!targetResult.Result.IsSuccess)
                return targetResult;

            var errors = _validationPolicy.ValidateText(command.Text);
            return errors.Count == 0
                ? targetResult
                : ValidatedOperation.Failed(PageOperationResult.ValidationFailed(errors));
        }

        async Task<ValidatedOperation> ValidateRenameCoreAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var targetResult = await ResolveWritablePageAsync(command.Target, token)
                .ConfigureAwait(false);
            if (!targetResult.Result.IsSuccess)
                return targetResult;

            var errors = _validationPolicy.ValidateName(command.Name);
            if (errors.Count > 0)
                return ValidatedOperation.Failed(PageOperationResult.ValidationFailed(errors));

            var duplicates = await targetResult.Context.PageRepository
                .ReadPagesAsync(command.NoteId, command.Name, token)
                .ConfigureAwait(false);
            if (duplicates.Count > 0)
            {
                return ValidatedOperation.Failed(
                    PageOperationResult.ValidationFailed(
                        new[] { PageErrorCode.DuplicateName }));
            }

            return targetResult;
        }

        Task<ValidatedOperation> ValidateDeleteCoreAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            return ResolveWritablePageAsync(command.Target, token);
        }

        ValidatedOperation ResolveWritableContext(NoteId noteId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var context = _contextResolver.Resolve(noteId);
            if (context == null)
                return ValidatedOperation.Failed(OwnerNotFound());
            if (context.IsReadOnly)
                return ValidatedOperation.Failed(ReadOnly());

            return ValidatedOperation.Succeeded(context);
        }

        async Task<ValidatedOperation> ResolveWritablePageAsync(
            PageReference target,
            CancellationToken token)
        {
            var contextResult = ResolveWritableContext(target.NoteId, token);
            if (!contextResult.Result.IsSuccess)
                return contextResult;

            var page = await contextResult.Context.PageRepository
                .ReadPageAsync(target.NoteId, target.PageId, token)
                .ConfigureAwait(false);
            return page == null
                ? ValidatedOperation.Failed(PageNotFound())
                : ValidatedOperation.Succeeded(contextResult.Context, page);
        }

        static PageOperationResult OwnerNotFound()
        {
            return PageOperationResult.Failed(
                PageOperationStatus.OwnerNotFound,
                PageErrorCode.OwnerNotFound);
        }

        static PageOperationResult PageNotFound()
        {
            return PageOperationResult.Failed(
                PageOperationStatus.PageNotFound,
                PageErrorCode.PageNotFound);
        }

        static PageOperationResult ReadOnly()
        {
            return PageOperationResult.Failed(
                PageOperationStatus.ReadOnly,
                PageErrorCode.ReadOnly);
        }

        sealed class ValidatedOperation
        {
            ValidatedOperation(
                PageOperationResult result,
                NoteContext context,
                Page page)
            {
                Result = result;
                Context = context;
                Page = page;
            }

            internal PageOperationResult Result { get; }

            internal NoteContext Context { get; }

            internal Page Page { get; }

            internal static ValidatedOperation Succeeded(
                NoteContext context,
                Page page = null)
            {
                return new ValidatedOperation(
                    PageOperationResult.Succeeded(page),
                    context,
                    page);
            }

            internal static ValidatedOperation Failed(PageOperationResult result)
            {
                return new ValidatedOperation(result, null, null);
            }
        }
    }
}
