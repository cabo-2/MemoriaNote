using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies page application coordination without a database.
/// </summary>
[TestFixture]
public sealed class PageUseCaseTests
{
    /// <summary>
    /// Verifies creation uses the command target rather than another available note.
    /// </summary>
    [Test]
    public async Task CreateAsync_UsesTheExplicitTargetNote()
    {
        var selectedId = CreateNoteId("selected");
        var targetId = CreateNoteId("target");
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
        var selectedId = CreateNoteId("selected-owner");
        var ownerId = CreateNoteId("actual-owner");
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
        var missingOwnerId = CreateNoteId("missing-owner");
        var ownerId = CreateNoteId("missing-page-owner");
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
        var noteId = CreateNoteId("read-only");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"));
        var useCase = CreateUseCase((noteId, true, repository));

        var create = await useCase.CreateAsync(
            new CreatePageCommand(noteId, "Created", "Text"),
            CancellationToken.None);
        var edit = await useCase.EditAsync(
            new EditPageCommand(noteId, pageId, "Edited"),
            CancellationToken.None);
        var rename = await useCase.RenameAsync(
            new RenamePageCommand(noteId, pageId, "Renamed"),
            CancellationToken.None);
        var delete = await useCase.DeleteAsync(
            new DeletePageCommand(noteId, pageId),
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
        var noteId = CreateNoteId("validation");
        var repository = new FakeNoteRepository(
            CreatePage(PageId.FromGuid(Guid.NewGuid()), "Existing", "Text"));
        var useCase = CreateUseCase((noteId, false, repository));

        var required = await useCase.ValidateCreateAsync(
            new CreatePageCommand(noteId, " ", "Text"),
            CancellationToken.None);
        var duplicate = await useCase.ValidateCreateAsync(
            new CreatePageCommand(noteId, "Existing", "Text"),
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
        var noteId = CreateNoteId("delete-race");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"))
        {
            DeleteResult = false
        };
        var useCase = CreateUseCase((noteId, false, repository));

        var result = await useCase.DeleteAsync(
            new DeletePageCommand(noteId, pageId),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PageOperationStatus.PageNotFound));
    }

    /// <summary>
    /// Verifies repository-side update races are returned as typed not-found failures.
    /// </summary>
    [Test]
    public async Task EditAsync_PageRemovedAfterValidation_ReturnsPageNotFound()
    {
        var noteId = CreateNoteId("update-race");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository(CreatePage(pageId, "Existing", "Text"))
        {
            ThrowMissingOnUpdate = true
        };
        var useCase = CreateUseCase((noteId, false, repository));

        var result = await useCase.EditAsync(
            new EditPageCommand(noteId, pageId, "Edited"),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PageOperationStatus.PageNotFound));
    }

    /// <summary>
    /// Verifies cancellation reaches repository-backed validation.
    /// </summary>
    [Test]
    public void ValidateCreateAsync_RepositoryCancellation_IsPropagated()
    {
        var noteId = CreateNoteId("cancellation");
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeNoteRepository
        {
            BeforeRead = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var useCase = CreateUseCase((noteId, false, repository));
        Func<Task> validate = () => useCase.ValidateCreateAsync(
            new CreatePageCommand(noteId, "Name", "Text"),
            cancellation.Token);

        Assert.That(validate, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>
    /// Verifies infrastructure failures are not converted into business results.
    /// </summary>
    [Test]
    public void ReadAsync_InfrastructureFailure_IsPropagated()
    {
        var noteId = CreateNoteId("failure");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var repository = new FakeNoteRepository
        {
            BeforeRead = _ => throw new InvalidOperationException("Database unavailable.")
        };
        var useCase = CreateUseCase((noteId, false, repository));
        Func<Task> read = () => useCase.ReadAsync(
            new PageReference(noteId, pageId),
            CancellationToken.None);

        Assert.That(read, Throws.TypeOf<InvalidOperationException>());
    }

    static PageUseCase CreateUseCase(
        params (NoteId NoteId, bool IsReadOnly, FakeNoteRepository Repository)[] contexts)
    {
        return new PageUseCase(
            new FakeContextResolver(contexts),
            new PageValidationPolicy());
    }

    static NoteId CreateNoteId(string name)
    {
        return NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }

    static Page CreatePage(PageId pageId, string name, string text)
    {
        var page = Page.Create(name, text);
        page.Guid = pageId.Value;
        return page;
    }

    sealed class FakeContextResolver : INoteContextResolver
    {
        readonly Dictionary<NoteId, NoteContext> _contexts;

        internal FakeContextResolver(
            IEnumerable<(NoteId NoteId, bool IsReadOnly, FakeNoteRepository Repository)> contexts)
        {
            _contexts = contexts.ToDictionary(
                item => item.NoteId,
                item => new NoteContext(item.NoteId, item.IsReadOnly, item.Repository));
        }

        public NoteContext Resolve(NoteId noteId)
        {
            return _contexts.GetValueOrDefault(noteId)!;
        }
    }

    sealed class FakeNoteRepository : INoteRepository
    {
        readonly List<Page> _pages;

        internal FakeNoteRepository(params Page[] pages)
        {
            _pages = pages.ToList();
        }

        internal IReadOnlyList<Page> Pages => _pages;

        internal int ReadCount { get; private set; }

        internal int MutationCount { get; private set; }

        internal bool DeleteResult { get; set; } = true;

        internal bool ThrowMissingOnUpdate { get; set; }

        internal Action<CancellationToken>? BeforeRead { get; set; }

        public Task<Page> ReadPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult(_pages.SingleOrDefault(page => page.Guid == pageId))!;
        }

        public Task<Page> ReadPageAsync(
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

        public Task<IReadOnlyList<Page>> ReadPagesAsync(
            string dataSource,
            string name,
            CancellationToken token)
        {
            ReadCount++;
            BeforeRead?.Invoke(token);
            return Task.FromResult<IReadOnlyList<Page>>(
                _pages.Where(page => page.Name == name).ToList());
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
            NoteId noteId,
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

        public Task<int> CountAsync(string dataSource, CancellationToken token)
        {
            return Task.FromResult(_pages.Count);
        }

        public Task<IReadOnlyList<Content>> ReadContentsAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            return Task.FromResult<IReadOnlyList<Content>>(
                _pages.Skip(skipCount).Take(takeCount).Select(page => page.GetContent()).ToList());
        }
    }
}
