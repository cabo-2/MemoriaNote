using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Coordinates counting and paged searches across ordered note repositories.
    /// </summary>
    public sealed class SearchUseCase : ISearchUseCase
    {
        readonly Func<NoteId, INoteSearchRepository> _repositoryResolver;

        /// <summary>
        /// Initializes a search use case that uses one repository for every note.
        /// </summary>
        /// <param name="repository">The note search repository.</param>
        public SearchUseCase(INoteSearchRepository repository)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            _repositoryResolver = _ => repository;
        }

        internal SearchUseCase(Func<NoteId, INoteSearchRepository> repositoryResolver)
        {
            _repositoryResolver = repositoryResolver ??
                throw new ArgumentNullException(nameof(repositoryResolver));
        }

        /// <inheritdoc/>
        public async Task<SearchPage> SearchAsync(
            SearchRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            token.ThrowIfCancellationRequested();
            var targets = new List<SearchTarget>(request.NoteIds.Count);
            var totalCount = 0;
            foreach (var noteId in request.NoteIds)
            {
                token.ThrowIfCancellationRequested();
                var repository = _repositoryResolver(noteId) ??
                    throw new InvalidOperationException("No search repository was found for the note.");
                var count = await repository.CountAsync(
                        noteId,
                        request.Query,
                        request.Method,
                        token)
                    .ConfigureAwait(false);
                if (count < 0)
                    throw new InvalidOperationException("A search repository returned a negative count.");

                totalCount = checked(totalCount + count);
                targets.Add(new SearchTarget(noteId, repository, count));
            }

            var items = new List<PageSummary>();
            var remainingOffset = request.Offset;
            var remainingLimit = request.Limit;
            foreach (var target in targets)
            {
                if (remainingLimit == 0)
                    break;
                if (remainingOffset >= target.Count)
                {
                    remainingOffset -= target.Count;
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var takeCount = Math.Min(
                    remainingLimit,
                    target.Count - remainingOffset);
                var noteItems = await target.Repository.SearchPageSummariesAsync(
                        target.NoteId,
                        request.Query,
                        request.Method,
                        remainingOffset,
                        takeCount,
                        token)
                    .ConfigureAwait(false);
                foreach (var item in noteItems)
                {
                    if (items.Count == request.Limit)
                        break;
                    items.Add(item);
                }

                remainingLimit = request.Limit - items.Count;
                remainingOffset = 0;
            }

            return new SearchPage(
                items,
                totalCount,
                request.Offset,
                request.Limit);
        }

        sealed class SearchTarget
        {
            internal SearchTarget(
                NoteId noteId,
                INoteSearchRepository repository,
                int count)
            {
                NoteId = noteId;
                Repository = repository;
                Count = count;
            }

            internal NoteId NoteId { get; }

            internal INoteSearchRepository Repository { get; }

            internal int Count { get; }
        }
    }
}
