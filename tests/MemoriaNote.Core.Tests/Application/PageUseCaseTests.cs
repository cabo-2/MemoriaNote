using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies page application coordination without a database.
/// </summary>
[TestFixture]
public sealed class PageUseCaseTests
{
    /// <summary>Verifies exact-name resolution returns only a unique owner-qualified page.</summary>
    [Test]
    public async Task ResolveAsync_ExactName_ClassifiesUniqueMissingAndConflict()
    {
        var notebookId = CreateNotebookId("resolve-name");
        var unique = CreatePage(PageId.FromGuid(Guid.NewGuid()), "Unique", "Body");
        var firstDuplicate = CreatePage(PageId.FromGuid(Guid.NewGuid()), "Duplicate", "First");
        var secondDuplicate = CreatePage(PageId.FromGuid(Guid.NewGuid()), "Duplicate", "Second");
        var useCase = CreateUseCase((
            notebookId,
            false,
            new FakeNoteRepository(unique, firstDuplicate, secondDuplicate)));

        var found = await useCase.ResolveAsync(
            new PageTargetRequest(notebookId, PageSelector.FromName("Unique")),
            CancellationToken.None);
        var missing = await useCase.ResolveAsync(
            new PageTargetRequest(notebookId, PageSelector.FromName("Missing")),
            CancellationToken.None);
        var conflict = await useCase.ResolveAsync(
            new PageTargetRequest(notebookId, PageSelector.FromName("Duplicate")),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found.Status, Is.EqualTo(PageTargetResolutionStatus.Success));
            Assert.That(found.Target?.NotebookId, Is.EqualTo(notebookId));
            Assert.That(found.Target?.PageId.Value, Is.EqualTo(unique.Guid));
            Assert.That(missing.Status, Is.EqualTo(PageTargetResolutionStatus.PageNotFound));
            Assert.That(conflict.Status, Is.EqualTo(PageTargetResolutionStatus.Conflict));
        }
    }

    /// <summary>Verifies Page ID prefix resolution is case-insensitive and rejects ambiguity.</summary>
    [Test]
    public async Task ResolveAsync_PageIdPrefix_ClassifiesUniqueAndConflict()
    {
        var notebookId = CreateNotebookId("resolve-id");
        var first = CreatePage(
            PageId.FromGuid(Guid.Parse("abcd0000-0000-0000-0000-000000000001")),
            "First",
            "Body");
        var second = CreatePage(
            PageId.FromGuid(Guid.Parse("abcd0000-0000-0000-0000-000000000002")),
            "Second",
            "Body");
        var useCase = CreateUseCase((
            notebookId,
            false,
            new FakeNoteRepository(first, second)));
        PageSelector.TryFromPageId("ABCD0000-0000-0000-0000-000000000001", out var fullId);
        PageSelector.TryFromPageId("ABCD", out var ambiguousPrefix);

        var found = await useCase.ResolveAsync(
            new PageTargetRequest(notebookId, fullId!),
            CancellationToken.None);
        var conflict = await useCase.ResolveAsync(
            new PageTargetRequest(notebookId, ambiguousPrefix!),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found.Status, Is.EqualTo(PageTargetResolutionStatus.Success));
            Assert.That(found.Target?.PageId.Value, Is.EqualTo(first.Guid));
            Assert.That(conflict.Status, Is.EqualTo(PageTargetResolutionStatus.Conflict));
        }
    }

    /// <summary>Verifies target resolution distinguishes an unavailable notebook.</summary>
    [Test]
    public async Task ResolveAsync_MissingOwner_ReturnsOwnerNotFound()
    {
        var result = await CreateUseCase().ResolveAsync(
            new PageTargetRequest(
                CreateNotebookId("missing-resolve-owner"),
                PageSelector.FromName("Page")),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PageTargetResolutionStatus.OwnerNotFound));
    }

    /// <summary>Verifies listing uses the explicitly owned repository and optional limit.</summary>
    [Test]
    public async Task ListAsync_UsesExplicitOwnerAndLimitWithoutMutation()
    {
        var selectedId = CreateNotebookId("selected-list");
        var targetId = CreateNotebookId("target-list");
        var selectedRepository = new FakeNoteRepository(
            CreatePage(PageId.FromGuid(Guid.NewGuid()), "Selected", "Body"));
        var targetRepository = new FakeNoteRepository(
            CreatePage(PageId.FromGuid(Guid.NewGuid()), "Target", "Body"));
        var useCase = CreateUseCase(
            (selectedId, false, selectedRepository),
            (targetId, true, targetRepository));

        var result = await useCase.ListAsync(
            new PageListRequest(targetId, 7),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(page => page.Name), Is.EqualTo(new[] { "Target" }));
            Assert.That(selectedRepository.ListCallCount, Is.Zero);
            Assert.That(targetRepository.ListCallCount, Is.EqualTo(1));
            Assert.That(targetRepository.LastSkipCount, Is.Zero);
            Assert.That(targetRepository.LastTakeCount, Is.EqualTo(7));
            Assert.That(targetRepository.MutationCount, Is.Zero);
        }
    }

    /// <summary>
    /// Verifies creation uses the command target rather than another available note.
    /// </summary>
    [Test]
    public async Task CreateAsync_UsesTheExplicitTargetNote()
    {
        var selectedId = CreateNotebookId("selected");
        var targetId = CreateNotebookId("target");
        var selectedRepository = new FakeNoteRepository();
        var targetRepository = new FakeNoteRepository();
        var useCase = CreateUseCase(
            (selectedId, false, selectedRepository),
            (targetId, false, targetRepository));

        var result = await useCase.CreateAsync(
            new CreatePageCommand(targetId, "Created", "Text"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(PageOperationStatus.Success));
            Assert.That(result.Page?.Name, Is.EqualTo("Created"));
            Assert.That(selectedRepository.Pages, Is.Empty);
            Assert.That(targetRepository.Pages.Single().Text, Is.EqualTo("Text"));
        }
    }

    /// <summary>
    /// Verifies owner-qualified commands update only their declared owner.
    /// </summary>
    [Test]
    public async Task EditRenameDeleteAsync_UseTheDeclaredOwner()
    {
        var selectedId = CreateNotebookId("selected-owner");
        var ownerId = CreateNotebookId("actual-owner");
        var sharedId = PageId.FromGuid(Guid.NewGuid());
        var selectedRepository = new FakeNoteRepository(CreatePage(sharedId, "Shared", "Selected"));
        var ownerRepository = new FakeNoteRepository(CreatePage(sharedId, "Shared", "Owner"));
        var useCase = CreateUseCase(
            (selectedId, false, selectedRepository),
            (ownerId, false, ownerRepository));

        var edit = await useCase.EditAsync(
            new EditPageCommand(ownerId, sharedId, "Edited"),
            CancellationToken.None);
        var rename = await useCase.RenameAsync(
            new RenamePageCommand(ownerId, sharedId, "Renamed"),
            CancellationToken.None);
        var delete = await useCase.DeleteAsync(
            new DeletePageCommand(ownerId, sharedId),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(edit.IsSuccess, Is.True);
            Assert.That(rename.IsSuccess, Is.True);
            Assert.That(delete.IsSuccess, Is.True);
            Assert.That(selectedRepository.Pages.Single().Text, Is.EqualTo("Selected"));
            Assert.That(selectedRepository.Pages.Single().Name, Is.EqualTo("Shared"));
            Assert.That(ownerRepository.Pages, Is.Empty);
        }
    }

    /// <summary>
    /// Verifies a note removed from the context is distinguishable from a missing page.
    /// </summary>
    [Test]
    public async Task ValidateEditAsync_ClassifiesMissingOwnerAndPage()
    {
        var missingOwnerId = CreateNotebookId("missing-owner");
        var ownerId = CreateNotebookId("missing-page-owner");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var useCase = CreateUseCase((ownerId, false, new FakeNoteRepository()));

        var missingOwner = await useCase.ValidateEditAsync(
            new EditPageCommand(missingOwnerId, pageId, "Text"),
            CancellationToken.None);
        var missingPage = await useCase.ValidateEditAsync(
            new EditPageCommand(ownerId, pageId, "Text"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingOwner.Status, Is.EqualTo(PageOperationStatus.OwnerNotFound));
            Assert.That(missingOwner.Errors, Is.EqualTo(new[] { PageErrorCode.OwnerNotFound }));
            Assert.That(missingPage.Status, Is.EqualTo(PageOperationStatus.PageNotFound));
            Assert.That(missingPage.Errors, Is.EqualTo(new[] { PageErrorCode.PageNotFound }));
        }
    }

    /// <summary>
    /// Verifies the current metadata snapshot prevents all mutations before page I/O.
    /// </summary>
    [Test]
    public async Task Mutations_ReadOnlySnapshot_PreventsRepositoryAccess()
    {
        var notebookId = CreateNotebookId("read-only");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"));
        var useCase = CreateUseCase((notebookId, true, repository));

        var create = await useCase.CreateAsync(
            new CreatePageCommand(notebookId, "Created", "Text"),
            CancellationToken.None);
        var edit = await useCase.EditAsync(
            new EditPageCommand(notebookId, pageId, "Edited"),
            CancellationToken.None);
        var rename = await useCase.RenameAsync(
            new RenamePageCommand(notebookId, pageId, "Renamed"),
            CancellationToken.None);
        var delete = await useCase.DeleteAsync(
            new DeletePageCommand(notebookId, pageId),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                new[] { create.Status, edit.Status, rename.Status, delete.Status },
                Is.All.EqualTo(PageOperationStatus.ReadOnly));
            Assert.That(repository.ReadCount, Is.Zero);
            Assert.That(repository.MutationCount, Is.Zero);
        }
    }

    /// <summary>
    /// Verifies pure and repository-backed name validation return typed errors.
    /// </summary>
    [Test]
    public async Task ValidateCreateAsync_ReturnsTypedNameErrorsWithoutMutation()
    {
        var notebookId = CreateNotebookId("validation");
        var repository = new FakeNoteRepository(
            CreatePage(PageId.FromGuid(Guid.NewGuid()), "Existing", "Text"));
        var useCase = CreateUseCase((notebookId, false, repository));

        var required = await useCase.ValidateCreateAsync(
            new CreatePageCommand(notebookId, " ", "Text"),
            CancellationToken.None);
        var duplicate = await useCase.ValidateCreateAsync(
            new CreatePageCommand(notebookId, "Existing", "Text"),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(required.Status, Is.EqualTo(PageOperationStatus.ValidationFailed));
            Assert.That(required.Errors, Is.EqualTo(new[] { PageErrorCode.NameRequired }));
            Assert.That(duplicate.Status, Is.EqualTo(PageOperationStatus.ValidationFailed));
            Assert.That(duplicate.Errors, Is.EqualTo(new[] { PageErrorCode.DuplicateName }));
            Assert.That(repository.MutationCount, Is.Zero);
        }
    }

    /// <summary>
    /// Verifies repository-side deletion races are returned as typed not-found failures.
    /// </summary>
    [Test]
    public async Task DeleteAsync_PageRemovedAfterValidation_ReturnsPageNotFound()
    {
        var notebookId = CreateNotebookId("delete-race");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"))
        {
            DeleteResult = false
        };
        var useCase = CreateUseCase((notebookId, false, repository));

        var result = await useCase.DeleteAsync(
            new DeletePageCommand(notebookId, pageId),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PageOperationStatus.PageNotFound));
    }

    /// <summary>
    /// Verifies repository-side update races are returned as typed not-found failures.
    /// </summary>
    [Test]
    public async Task EditAsync_PageRemovedAfterValidation_ReturnsPageNotFound()
    {
        var notebookId = CreateNotebookId("update-race");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"))
        {
            ThrowMissingOnUpdate = true
        };
        var useCase = CreateUseCase((notebookId, false, repository));

        var result = await useCase.EditAsync(
            new EditPageCommand(notebookId, pageId, "Edited"),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PageOperationStatus.PageNotFound));
    }

    /// <summary>
    /// Verifies cancellation reaches repository-backed validation.
    /// </summary>
    [Test]
    public void ValidateCreateAsync_RepositoryCancellation_IsPropagated()
    {
        var notebookId = CreateNotebookId("cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeNoteRepository
        {
            BeforeRead = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var useCase = CreateUseCase((notebookId, false, repository));
        Func<Task> validate = () => useCase.ValidateCreateAsync(
            new CreatePageCommand(notebookId, "Name", "Text"),
            cancellation.Token);

        Assert.That(validate, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>
    /// Verifies infrastructure failures are not converted into business results.
    /// </summary>
    [Test]
    public void ReadAsync_InfrastructureFailure_IsPropagated()
    {
        var notebookId = CreateNotebookId("failure");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository
        {
            BeforeRead = _ => throw new InvalidOperationException("Database unavailable.")
        };
        var useCase = CreateUseCase((notebookId, false, repository));
        Func<Task> read = () => useCase.ReadAsync(
            new PageReference(notebookId, pageId),
            CancellationToken.None);

        Assert.That(read, Throws.TypeOf<InvalidOperationException>());
    }

    static PageUseCase CreateUseCase(
        params (NotebookId NotebookId, bool IsReadOnly, FakeNoteRepository Repository)[] contexts)
    {
        return new PageUseCase(
            new FakeContextResolver(contexts),
            new PageValidationPolicy());
    }

    static NotebookId CreateNotebookId(string name)
    {
        return NotebookId.FromDatabasePath(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }

    static Page CreatePage(PageId pageId, string name, string text)
    {
        var page = Page.Create(name, text);
        page.Guid = pageId.Value;
        return page;
    }

    sealed class FakeContextResolver : INotebookContextResolver
    {
        readonly Dictionary<NotebookId, NotebookContext> _contexts;

        internal FakeContextResolver(
            IEnumerable<(NotebookId NotebookId, bool IsReadOnly, FakeNoteRepository Repository)> contexts)
        {
            _contexts = contexts.ToDictionary(
                item => item.NotebookId,
                item => new NotebookContext(item.NotebookId, item.IsReadOnly, item.Repository));
        }

        public NotebookContext Resolve(NotebookId notebookId)
        {
            return _contexts.GetValueOrDefault(notebookId)!;
        }
    }

    sealed class FakeNoteRepository : IPageRepository
    {
        readonly List<Page> _pages;

        internal FakeNoteRepository(params Page[] pages)
        {
            _pages = pages.ToList();
        }

        internal IReadOnlyList<Page> Pages => _pages;

        internal int ReadCount { get; private set; }

        internal int MutationCount { get; private set; }

        internal int ListCallCount { get; private set; }

        internal int LastSkipCount { get; private set; }

        internal int LastTakeCount { get; private set; }

        internal bool DeleteResult { get; set; } = true;

        internal bool ThrowMissingOnUpdate { get; set; }

        internal Action<CancellationToken>? BeforeRead { get; set; }

        public Task<Page> FindPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult(_pages.SingleOrDefault(page => page.Guid == pageId))!;
        }

        public Task<Page> FindPageAsync(
            string dataSource,
            string name,
            int index,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult(
                _pages.SingleOrDefault(page => page.Name == name && page.Index == index))!;
        }

        public Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
            string dataSource,
            string name,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult<IReadOnlyList<Page>>(
                _pages.Where(page => page.Name == name).ToList());
        }

        public Task<IReadOnlyList<Page>> ListPagesByIdPrefixAsync(
            string dataSource,
            string pageIdPrefix,
            int maximumCount,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult<IReadOnlyList<Page>>(
                _pages
                    .Where(page => page.Guid.ToString("N").StartsWith(
                        pageIdPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(page => page.Guid)
                    .Take(maximumCount)
                    .ToList());
        }

        public Task<Page> CreatePageAsync(
            string dataSource,
            string name,
            string text,
            string? directory,
            CancellationToken token)
        {
            MutationCount++;
            token.ThrowIfCancellationRequested();
            var page = Page.Create(name, text, directory);
            _pages.Add(page);
            return Task.FromResult(page);
        }

        public Task<Page> UpdatePageAsync(
            string dataSource,
            Page page,
            CancellationToken token)
        {
            MutationCount++;
            token.ThrowIfCancellationRequested();
            if (ThrowMissingOnUpdate)
                throw new KeyNotFoundException();

            return Task.FromResult(page);
        }

        public Task DeletePageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            MutationCount++;
            token.ThrowIfCancellationRequested();
            _pages.RemoveAll(page => page.Guid == pageId);
            return Task.CompletedTask;
        }

        public Task<bool> TryDeletePageAsync(
            NotebookId notebookId,
            PageId pageId,
            CancellationToken token)
        {
            MutationCount++;
            token.ThrowIfCancellationRequested();
            if (!DeleteResult)
                return Task.FromResult(false);

            var deleted = _pages.RemoveAll(page => page.Guid == pageId.Value) > 0;
            return Task.FromResult(deleted);
        }

        public Task<int> CountPagesAsync(string dataSource, CancellationToken token)
        {
            return Task.FromResult(_pages.Count);
        }

        public Task<IReadOnlyList<PageSummary>> ListPageSummariesAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            ListCallCount++;
            LastSkipCount = skipCount;
            LastTakeCount = takeCount;
            var notebookId = NotebookId.FromDatabasePath(dataSource);
            return Task.FromResult<IReadOnlyList<PageSummary>>(
                _pages
                    .Skip(skipCount)
                    .Take(takeCount)
                    .Select(page => new PageSummary(
                        notebookId,
                        PageId.FromGuid(page.Guid),
                        page.Name,
                        page.Index,
                        page.TagDict,
                        page.ContentType,
                        page.CreateTime,
                        page.UpdateTime,
                        page.IsErased))
                    .ToList());
        }
    }
}
