using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Cli
{
    internal interface ICurrentNotebookApplicationService
    {
        NotebookDescriptor Notebook { get; }

        INotebookMetadataApplication Metadata { get; }
    }

    internal sealed class SchemaV2ApplicationServiceAdapter :
        IMemoriaNoteApplicationService,
        ICurrentNotebookApplicationService
    {
        readonly NotebookApplicationSession _session;
        readonly PageValidationPolicy _validationPolicy = new PageValidationPolicy();

        internal SchemaV2ApplicationServiceAdapter(NotebookApplicationSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public NotebookDescriptor Notebook => _session.Notebook;

        public INotebookMetadataApplication Metadata => _session.Metadata;

        public async Task<IReadOnlyList<PageSummary>> ListPagesAsync(
            PageListRequest request,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            EnsureOwner(request.NotebookId);
            var result = await _session.Pages.QuerySummariesAsync(
                    new NotebookPageQueryRequest(limit: request.Limit),
                    token)
                .ConfigureAwait(false);
            return Require(result).Select(Map).ToArray();
        }

        public async Task<PageTargetResolution> ResolvePageAsync(
            PageTargetRequest request,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Owns(request.NotebookId))
            {
                return PageTargetResolution.Failed(
                    PageTargetResolutionStatus.OwnerNotFound);
            }

            var result = request.Selector.IsName
                ? await _session.Pages.GetByNameAsync(request.Selector.Name, token)
                    .ConfigureAwait(false)
                : await _session.Pages.ResolveByIdPrefixAsync(
                        request.Selector.PageIdPrefix,
                        token)
                    .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return PageTargetResolution.Succeeded(
                    new PageReference(request.NotebookId, result.Value!.PageId));
            }

            return result.Failure!.Code switch
            {
                "page.not_found" => PageTargetResolution.Failed(
                    PageTargetResolutionStatus.PageNotFound),
                "page.ambiguous_id_prefix" => PageTargetResolution.Failed(
                    PageTargetResolutionStatus.Conflict),
                _ => throw FailureException(result.Failure)
            };
        }

        public Task<SearchPage> SearchAsync(
            SearchRequest request,
            CancellationToken token) => throw new NotSupportedException(
                "The compatibility search surface is not used by the schema v2 CLI composition.");

        public int? GetNextPageOffset(int offset, int limit, int totalCount)
        {
            var next = checked(offset + limit);
            return next < totalCount ? next : null;
        }

        public int? GetPreviousPageOffset(int offset, int limit) =>
            offset <= 0 ? null : Math.Max(0, offset - limit);

        public async Task<PageOperationResult> ReadAsync(
            PageReference target,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (!Owns(target.NotebookId))
                return OwnerNotFound();
            return Map(await _session.Pages.GetByIdAsync(target.PageId, token)
                .ConfigureAwait(false));
        }

        public async Task<PageOperationResult> ValidateCreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            var ownerFailure = await ValidateWritableOwnerAsync(command.NotebookId, token)
                .ConfigureAwait(false);
            if (ownerFailure is not null)
                return ownerFailure;

            var errors = _validationPolicy.ValidateName(command.Name).ToList();
            errors.AddRange(_validationPolicy.ValidateText(command.Text));
            if (errors.Count > 0)
                return PageOperationResult.ValidationFailed(errors);

            var existing = await _session.Pages.GetByNameAsync(command.Name, token)
                .ConfigureAwait(false);
            if (existing.IsSuccess)
            {
                return PageOperationResult.ValidationFailed(
                    new[] { PageErrorCode.DuplicateName });
            }
            if (existing.Failure!.Code != "page.not_found")
                throw FailureException(existing.Failure);
            return PageOperationResult.Succeeded();
        }

        public async Task<PageOperationResult> ValidateEditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            var ownerFailure = await ValidateWritableOwnerAsync(command.NotebookId, token)
                .ConfigureAwait(false);
            if (ownerFailure is not null)
                return ownerFailure;
            return Map(await _session.Pages.GetByIdAsync(command.PageId, token)
                .ConfigureAwait(false));
        }

        public async Task<PageOperationResult> ValidateRenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            var page = await ValidateEditAsync(
                    new EditPageCommand(command.NotebookId, command.PageId, string.Empty),
                    token)
                .ConfigureAwait(false);
            if (!page.IsSuccess)
                return page;
            var errors = _validationPolicy.ValidateName(command.Name);
            if (errors.Count > 0)
                return PageOperationResult.ValidationFailed(errors);
            var duplicate = await _session.Pages.GetByNameAsync(command.Name, token)
                .ConfigureAwait(false);
            if (duplicate.IsSuccess && duplicate.Value!.PageId != command.PageId)
            {
                return PageOperationResult.ValidationFailed(
                    new[] { PageErrorCode.DuplicateName });
            }
            if (!duplicate.IsSuccess && duplicate.Failure!.Code != "page.not_found")
                throw FailureException(duplicate.Failure);
            return page;
        }

        public Task<PageOperationResult> ValidateDeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            return ValidateEditAsync(
                new EditPageCommand(command.NotebookId, command.PageId, string.Empty),
                token);
        }

        public async Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            EnsureOwner(command.NotebookId);
            return Map(await _session.Pages.CreateAsync(
                    new NotebookPageCreateRequest(
                        command.Name,
                        command.Text ?? string.Empty,
                        Array.Empty<string>()),
                    token)
                .ConfigureAwait(false));
        }

        public async Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            EnsureOwner(command.NotebookId);
            var current = await _session.Pages.GetByIdAsync(command.PageId, token)
                .ConfigureAwait(false);
            if (!current.IsSuccess)
                return Map(current);
            if (command.HasExpectedText &&
                !string.Equals(
                    current.Value!.Text,
                    command.ExpectedText,
                    StringComparison.Ordinal))
            {
                return PageOperationResult.Failed(
                    PageOperationStatus.Conflict,
                    PageErrorCode.ConcurrentEdit);
            }

            return Map(await _session.Pages.ReplaceAsync(
                    command.PageId,
                    current.Value!.Revision,
                    new NotebookPageReplaceRequest(
                        current.Value.Name,
                        command.Text ?? string.Empty,
                        current.Value.Tags),
                    token)
                .ConfigureAwait(false));
        }

        public async Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            EnsureOwner(command.NotebookId);
            var current = await _session.Pages.GetByIdAsync(command.PageId, token)
                .ConfigureAwait(false);
            if (!current.IsSuccess)
                return Map(current);
            return Map(await _session.Pages.ReplaceAsync(
                    command.PageId,
                    current.Value!.Revision,
                    new NotebookPageReplaceRequest(
                        command.Name,
                        current.Value.Text,
                        current.Value.Tags),
                    token)
                .ConfigureAwait(false));
        }

        public async Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(command);
            EnsureOwner(command.NotebookId);
            var current = await _session.Pages.GetByIdAsync(command.PageId, token)
                .ConfigureAwait(false);
            if (!current.IsSuccess)
                return Map(current);
            var deleted = await _session.Pages.DeleteAsync(
                    command.PageId,
                    current.Value!.Revision,
                    token)
                .ConfigureAwait(false);
            if (!deleted.IsSuccess)
                return MapFailure(deleted.Failure!);
            return PageOperationResult.Succeeded();
        }

        async Task<PageOperationResult> ValidateWritableOwnerAsync(
            NotebookId notebookId,
            CancellationToken token)
        {
            if (!Owns(notebookId))
                return OwnerNotFound();
            var metadata = await _session.Metadata.GetAsync(token).ConfigureAwait(false);
            var snapshot = Require(metadata);
            return snapshot.Values.TryGetValue("ReadOnly", out var value) &&
                   string.Equals(value, bool.TrueString, StringComparison.Ordinal)
                ? PageOperationResult.Failed(
                    PageOperationStatus.ReadOnly,
                    PageErrorCode.ReadOnly)
                : null;
        }

        bool Owns(NotebookId notebookId) =>
            notebookId is not null && notebookId == _session.Notebook.NotebookId;

        void EnsureOwner(NotebookId notebookId)
        {
            if (!Owns(notebookId))
                throw new InvalidOperationException("The notebook is not in this session.");
        }

        static T Require<T>(NotebookOperationResult<T> result)
        {
            if (!result.IsSuccess)
                throw FailureException(result.Failure!);
            return result.Value!;
        }

        static PageOperationResult Map(NotebookOperationResult<NotebookPage> result) =>
            result.IsSuccess
                ? PageOperationResult.Succeeded(Map(result.Value!))
                : MapFailure(result.Failure!);

        static PageOperationResult MapFailure(NotebookFailure failure)
        {
            return failure.Code switch
            {
                "page.not_found" => PageOperationResult.Failed(
                    PageOperationStatus.PageNotFound,
                    PageErrorCode.PageNotFound),
                "notebook.read_only" => PageOperationResult.Failed(
                    PageOperationStatus.ReadOnly,
                    PageErrorCode.ReadOnly),
                "page.name_conflict" => PageOperationResult.ValidationFailed(
                    new[] { PageErrorCode.DuplicateName }),
                "page.revision_conflict" => PageOperationResult.Failed(
                    PageOperationStatus.Conflict,
                    PageErrorCode.ConcurrentEdit),
                _ => throw FailureException(failure)
            };
        }

        static Exception FailureException(NotebookFailure failure) =>
            failure.Category switch
            {
                NotebookFailureCategory.Validation => new CliValidationException(failure.Message),
                NotebookFailureCategory.NotFound => new FileNotFoundException(failure.Message),
                NotebookFailureCategory.Conflict => new InvalidOperationException(failure.Message),
                NotebookFailureCategory.Format => new InvalidDataException(failure.Message),
                NotebookFailureCategory.AccessOrBusy => new IOException(failure.Message),
                NotebookFailureCategory.UnexpectedStorage => new IOException(failure.Message),
                NotebookFailureCategory.Timeout => new TimeoutException(failure.Message),
                _ => new InvalidOperationException(failure.Message)
            };

        static Page Map(NotebookPage page)
        {
            var mapped = new Page
            {
                Guid = page.PageId.Value,
                Name = page.Name,
                Index = 1,
                ContentType = "T",
                CreateTime = page.CreateTimeUtc.UtcDateTime,
                UpdateTime = page.UpdateTimeUtc.UtcDateTime,
                IsErased = false,
                Text = page.Text
            };
            foreach (var label in page.Tags)
                mapped.TagDict[label] = string.Empty;
            return mapped;
        }

        static PageSummary Map(NotebookPageSummary page) => new PageSummary(
            page.NotebookId,
            page.PageId,
            page.Name,
            index: 1,
            tags: null,
            contentType: "T",
            page.CreateTimeUtc.UtcDateTime,
            page.UpdateTimeUtc.UtcDateTime,
            isErased: false);

        static PageOperationResult OwnerNotFound() => PageOperationResult.Failed(
            PageOperationStatus.OwnerNotFound,
            PageErrorCode.OwnerNotFound);
    }
}
