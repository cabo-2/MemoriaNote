using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies the UI-independent application facade without Reactive scheduling or a database.
/// </summary>
[TestFixture]
public sealed class MemoriaNoteApplicationServiceTests
{
    /// <summary>
    /// Verifies search requests and cancellation tokens are delegated unchanged.
    /// </summary>
    [Test]
    public async Task SearchAsync_DelegatesTheTypedRequest()
    {
        var noteId = CreateNoteId("search");
        var request = SearchRequest.ForNote(
            "query",
            SearchMethodType.Heading,
            noteId,
            2,
            5);
        using var cancellation = new CancellationTokenSource();
        var expected = new SearchPage(Array.Empty<PageSummary>(), 7, 2, 5);
        var searchUseCase = new FakeSearchUseCase(expected);
        var service = new MemoriaNoteApplicationService(
            searchUseCase,
            new FakePageUseCase());

        var actual = await service.SearchAsync(request, cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(searchUseCase.Request, Is.SameAs(request));
            Assert.That(searchUseCase.Token, Is.EqualTo(cancellation.Token));
        }
    }

    /// <summary>
    /// Verifies next and previous offsets honor first, last, and partial pages.
    /// </summary>
    [Test]
    public void PagingOffsets_StayWithinSearchBoundaries()
    {
        var service = CreateService();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.GetNextPageOffset(0, 10, 21), Is.EqualTo(10));
            Assert.That(service.GetNextPageOffset(20, 10, 21), Is.Null);
            Assert.That(service.GetNextPageOffset(0, 10, 10), Is.Null);
            Assert.That(service.GetNextPageOffset(0, 0, 10), Is.Null);
            Assert.That(service.GetPreviousPageOffset(20, 10), Is.EqualTo(10));
            Assert.That(service.GetPreviousPageOffset(5, 10), Is.Zero);
            Assert.That(service.GetPreviousPageOffset(0, 10), Is.Null);
            Assert.That(service.GetPreviousPageOffset(10, 0), Is.Null);
        }
    }

    /// <summary>
    /// Verifies invalid paging values are rejected before any search is run.
    /// </summary>
    [Test]
    public void PagingOffsets_InvalidValuesAreRejected()
    {
        var service = CreateService();
        Action negativeOffset = () => service.GetNextPageOffset(-1, 10, 20);
        Action negativeLimit = () => service.GetNextPageOffset(0, -1, 20);
        Action negativeTotal = () => service.GetNextPageOffset(0, 10, -1);
        Action negativePreviousOffset = () => service.GetPreviousPageOffset(-1, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(negativeOffset, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(negativeLimit, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(negativeTotal, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                negativePreviousOffset,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    /// <summary>
    /// Verifies page reads, validation, and mutations return the page use case results.
    /// </summary>
    [Test]
    public async Task PageOperations_DelegateTypedCommandsAndResults()
    {
        var noteId = CreateNoteId("pages");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var reference = new PageReference(noteId, pageId);
        var create = new CreatePageCommand(noteId, "Created", "Text");
        var edit = new EditPageCommand(noteId, pageId, "Edited");
        var rename = new RenamePageCommand(noteId, pageId, "Renamed");
        var delete = new DeletePageCommand(noteId, pageId);
        var pageUseCase = new FakePageUseCase();
        var service = new MemoriaNoteApplicationService(
            new FakeSearchUseCase(new SearchPage(Array.Empty<PageSummary>(), 0, 0, 0)),
            pageUseCase);

        await service.ReadAsync(reference, CancellationToken.None);
        await service.ValidateCreateAsync(create, CancellationToken.None);
        await service.ValidateEditAsync(edit, CancellationToken.None);
        await service.ValidateRenameAsync(rename, CancellationToken.None);
        await service.ValidateDeleteAsync(delete, CancellationToken.None);
        await service.CreateAsync(create, CancellationToken.None);
        await service.EditAsync(edit, CancellationToken.None);
        await service.RenameAsync(rename, CancellationToken.None);
        await service.DeleteAsync(delete, CancellationToken.None);

        Assert.That(
            pageUseCase.Calls,
            Is.EqualTo(new[]
            {
                "read",
                "validate-create",
                "validate-edit",
                "validate-rename",
                "validate-delete",
                "create",
                "edit",
                "rename",
                "delete"
            }));
    }

    /// <summary>
    /// Verifies infrastructure and cancellation exceptions remain observable by callers.
    /// </summary>
    [Test]
    public void Failures_AreNotConvertedIntoPresentationResults()
    {
        var request = SearchRequest.ForWorkspace(
            "query",
            SearchMethodType.Heading,
            Array.Empty<NoteId>(),
            0,
            10);
        var infrastructure = new IOException("Database unavailable.");
        var failedService = new MemoriaNoteApplicationService(
            new ThrowingSearchUseCase(infrastructure),
            new FakePageUseCase());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceledService = new MemoriaNoteApplicationService(
            new ThrowingSearchUseCase(new OperationCanceledException(cancellation.Token)),
            new FakePageUseCase());

        Func<Task> infrastructureAction = () =>
            failedService.SearchAsync(request, CancellationToken.None);
        Func<Task> cancellationAction = () =>
            canceledService.SearchAsync(request, cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                infrastructureAction,
                Throws.TypeOf<IOException>().With.Message.EqualTo(infrastructure.Message));
            Assert.That(
                cancellationAction,
                Throws.InstanceOf<OperationCanceledException>());
        }
    }

    /// <summary>
    /// Verifies the public application API does not expose presentation-specific types.
    /// </summary>
    [Test]
    public void PublicApi_DoesNotExposePresentationContracts()
    {
        var forbiddenNames = new[]
        {
            "System.Action",
            "System.Func`1",
            "ReactiveUI.ReactiveCommand",
            typeof(Content).FullName,
            typeof(SearchResult).FullName,
            typeof(TextManageResult).FullName
        };
        var apiTypes = typeof(IMemoriaNoteApplicationService)
            .GetMethods()
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        Assert.That(
            apiTypes.Select(type => type.FullName).Intersect(forbiddenNames),
            Is.Empty);
    }

    static MemoriaNoteApplicationService CreateService()
    {
        return new MemoriaNoteApplicationService(
            new FakeSearchUseCase(new SearchPage(Array.Empty<PageSummary>(), 0, 0, 0)),
            new FakePageUseCase());
    }

    static NoteId CreateNoteId(string name)
    {
        return NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }

    sealed class FakeSearchUseCase : ISearchUseCase
    {
        readonly SearchPage _result;

        internal FakeSearchUseCase(SearchPage result)
        {
            _result = result;
        }

        internal SearchRequest? Request { get; private set; }

        internal CancellationToken Token { get; private set; }

        public Task<SearchPage> SearchAsync(SearchRequest request, CancellationToken token)
        {
            Request = request;
            Token = token;
            return Task.FromResult(_result);
        }
    }

    sealed class ThrowingSearchUseCase : ISearchUseCase
    {
        readonly Exception _exception;

        internal ThrowingSearchUseCase(Exception exception)
        {
            _exception = exception;
        }

        public Task<SearchPage> SearchAsync(SearchRequest request, CancellationToken token)
        {
            return Task.FromException<SearchPage>(_exception);
        }
    }

    sealed class FakePageUseCase : IPageUseCase
    {
        internal List<string> Calls { get; } = new();

        public Task<PageOperationResult> ReadAsync(
            PageReference target,
            CancellationToken token) => Record("read");

        public Task<PageOperationResult> ValidateCreateAsync(
            CreatePageCommand command,
            CancellationToken token) => Record("validate-create");

        public Task<PageOperationResult> ValidateEditAsync(
            EditPageCommand command,
            CancellationToken token) => Record("validate-edit");

        public Task<PageOperationResult> ValidateRenameAsync(
            RenamePageCommand command,
            CancellationToken token) => Record("validate-rename");

        public Task<PageOperationResult> ValidateDeleteAsync(
            DeletePageCommand command,
            CancellationToken token) => Record("validate-delete");

        public Task<PageOperationResult> CreateAsync(
            CreatePageCommand command,
            CancellationToken token) => Record("create");

        public Task<PageOperationResult> EditAsync(
            EditPageCommand command,
            CancellationToken token) => Record("edit");

        public Task<PageOperationResult> RenameAsync(
            RenamePageCommand command,
            CancellationToken token) => Record("rename");

        public Task<PageOperationResult> DeleteAsync(
            DeletePageCommand command,
            CancellationToken token) => Record("delete");

        Task<PageOperationResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(PageOperationResult.Succeeded());
        }
    }
}
