namespace MemoriaNote.Cli.Tests;

internal sealed class StubNotebookTargetSessionResolver : INotebookTargetSessionResolver
{
    readonly ApplicationSession _session;

    internal StubNotebookTargetSessionResolver(ApplicationSession session)
    {
        _session = session;
    }

    internal int ResolveCount { get; private set; }

    internal string? WorkspaceOption { get; private set; }

    internal string? NotebookOption { get; private set; }

    public Task<ApplicationSession> ResolveAsync(
        string workspaceOption,
        string notebookOption,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResolveCount++;
        WorkspaceOption = workspaceOption;
        NotebookOption = notebookOption;
        return Task.FromResult(_session);
    }
}

/// <summary>Provides configurable application behavior for CLI presentation tests.</summary>
internal sealed class StubApplicationService : IMemoriaNoteApplicationService
{
    internal Func<PageListRequest, CancellationToken, Task<IReadOnlyList<PageSummary>>> ListPagesAsyncHandler { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<PageSummary>>(Array.Empty<PageSummary>());

    internal int ListPagesAsyncCallCount { get; private set; }

    internal Func<PageTargetRequest, CancellationToken, Task<PageTargetResolution>> ResolvePageAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(
            PageTargetResolution.Failed(PageTargetResolutionStatus.PageNotFound));

    internal int ResolvePageAsyncCallCount { get; private set; }

    internal Func<SearchRequest, CancellationToken, Task<SearchPage>> SearchAsyncHandler { get; set; } =
        (request, _) => Task.FromResult(
            new SearchPage(Array.Empty<PageSummary>(), 0, request.Offset, request.Limit));

    internal int SearchAsyncCallCount { get; private set; }

    internal Func<PageReference, CancellationToken, Task<PageOperationResult>> ReadAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int ReadAsyncCallCount { get; private set; }

    internal Func<CreatePageCommand, CancellationToken, Task<PageOperationResult>> ValidateCreateAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int ValidateCreateAsyncCallCount { get; private set; }

    internal Func<EditPageCommand, CancellationToken, Task<PageOperationResult>> ValidateEditAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int ValidateEditAsyncCallCount { get; private set; }

    internal Func<RenamePageCommand, CancellationToken, Task<PageOperationResult>> ValidateRenameAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal Func<DeletePageCommand, CancellationToken, Task<PageOperationResult>> ValidateDeleteAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int ValidateDeleteAsyncCallCount { get; private set; }

    internal Func<CreatePageCommand, CancellationToken, Task<PageOperationResult>> CreateAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int CreateAsyncCallCount { get; private set; }

    internal Func<EditPageCommand, CancellationToken, Task<PageOperationResult>> EditAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int EditAsyncCallCount { get; private set; }

    internal Func<RenamePageCommand, CancellationToken, Task<PageOperationResult>> RenameAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int RenameAsyncCallCount { get; private set; }

    internal Func<DeletePageCommand, CancellationToken, Task<PageOperationResult>> DeleteAsyncHandler { get; set; } =
        (_, _) => Task.FromResult(PageOperationResult.Succeeded());

    internal int DeleteAsyncCallCount { get; private set; }

    public Task<IReadOnlyList<PageSummary>> ListPagesAsync(
        PageListRequest request,
        CancellationToken token)
    {
        ListPagesAsyncCallCount++;
        return ListPagesAsyncHandler(request, token);
    }

    public Task<PageTargetResolution> ResolvePageAsync(
        PageTargetRequest request,
        CancellationToken token)
    {
        ResolvePageAsyncCallCount++;
        return ResolvePageAsyncHandler(request, token);
    }

    public Task<SearchPage> SearchAsync(SearchRequest request, CancellationToken token)
    {
        SearchAsyncCallCount++;
        return SearchAsyncHandler(request, token);
    }

    public int? GetNextPageOffset(int offset, int limit, int totalCount)
    {
        if (limit == 0 || offset >= totalCount || limit >= totalCount - offset)
            return null;

        return offset + limit;
    }

    public int? GetPreviousPageOffset(int offset, int limit)
    {
        if (offset == 0 || limit == 0)
            return null;

        return Math.Max(0, offset - limit);
    }

    public Task<PageOperationResult> ReadAsync(PageReference target, CancellationToken token)
    {
        ReadAsyncCallCount++;
        return ReadAsyncHandler(target, token);
    }

    public Task<PageOperationResult> ValidateCreateAsync(
        CreatePageCommand command,
        CancellationToken token)
    {
        ValidateCreateAsyncCallCount++;
        return ValidateCreateAsyncHandler(command, token);
    }

    public Task<PageOperationResult> ValidateEditAsync(
        EditPageCommand command,
        CancellationToken token)
    {
        ValidateEditAsyncCallCount++;
        return ValidateEditAsyncHandler(command, token);
    }

    public Task<PageOperationResult> ValidateRenameAsync(
        RenamePageCommand command,
        CancellationToken token)
    {
        return ValidateRenameAsyncHandler(command, token);
    }

    public Task<PageOperationResult> ValidateDeleteAsync(
        DeletePageCommand command,
        CancellationToken token)
    {
        ValidateDeleteAsyncCallCount++;
        return ValidateDeleteAsyncHandler(command, token);
    }

    public Task<PageOperationResult> CreateAsync(
        CreatePageCommand command,
        CancellationToken token)
    {
        CreateAsyncCallCount++;
        return CreateAsyncHandler(command, token);
    }

    public Task<PageOperationResult> EditAsync(
        EditPageCommand command,
        CancellationToken token)
    {
        EditAsyncCallCount++;
        return EditAsyncHandler(command, token);
    }

    public Task<PageOperationResult> RenameAsync(
        RenamePageCommand command,
        CancellationToken token)
    {
        RenameAsyncCallCount++;
        return RenameAsyncHandler(command, token);
    }

    public Task<PageOperationResult> DeleteAsync(
        DeletePageCommand command,
        CancellationToken token)
    {
        DeleteAsyncCallCount++;
        return DeleteAsyncHandler(command, token);
    }
}
