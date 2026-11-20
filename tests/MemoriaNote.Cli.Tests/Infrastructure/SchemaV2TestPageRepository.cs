using MemoriaNote.Application;
using MemoriaNote.Domain;

namespace MemoriaNote.Cli.Tests.Infrastructure;

internal sealed class SchemaV2TestPage
{
    internal SchemaV2TestPage(NotebookPage page)
    {
        PageId = page.PageId;
        Guid = page.PageId.Value;
        Name = page.Name;
        Text = page.Text;
        Tags = page.Tags;
        TagDict = page.Tags.ToDictionary(tag => tag, tag => tag);
        CreateTime = page.CreateTimeUtc.UtcDateTime;
        UpdateTime = page.UpdateTimeUtc.UtcDateTime;
    }

    internal PageId PageId { get; }
    internal Guid Guid { get; }
    internal string Name { get; }
    internal string Text { get; }
    internal IReadOnlyList<string> Tags { get; }
    internal IReadOnlyDictionary<string, string> TagDict { get; }
    internal DateTime CreateTime { get; }
    internal DateTime UpdateTime { get; }
}

/// <summary>Provides schema v2 page operations used by CLI integration tests.</summary>
internal sealed class SchemaV2TestPageRepository
{
    internal async Task<SchemaV2TestPage> CreatePageAsync(
        string databasePath,
        string heading,
        string body,
        string? directory,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tags = string.IsNullOrEmpty(directory) ? Array.Empty<string>() : [directory];
        return new SchemaV2TestPage(await SchemaV2TestNotebook.CreatePageAsync(
                databasePath,
                heading,
                body,
                tags)
            .ConfigureAwait(false));
    }

    internal async Task<SchemaV2TestPage?> FindPageAsync(
        string databasePath,
        Guid pageId,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = await SchemaV2TestNotebook.FindByIdAsync(
                databasePath,
                PageId.FromGuid(pageId))
            .ConfigureAwait(false);
        return page == null ? null : new SchemaV2TestPage(page);
    }

    internal async Task<SchemaV2TestPage?> FindPageAsync(
        string databasePath,
        string heading,
        int ordinal,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ordinal != 1)
            return null;
        var page = await SchemaV2TestNotebook.FindByNameAsync(databasePath, heading)
            .ConfigureAwait(false);
        return page == null ? null : new SchemaV2TestPage(page);
    }

    internal async Task<IReadOnlyList<SchemaV2TestPage>> ListPagesByHeadingAsync(
        string databasePath,
        string heading,
        CancellationToken token)
    {
        var page = await FindPageAsync(databasePath, heading, 1, token)
            .ConfigureAwait(false);
        return page == null ? Array.Empty<SchemaV2TestPage>() : [page];
    }

    internal async Task<int> CountPagesAsync(
        string databasePath,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return (await SchemaV2TestNotebook.ListAsync(databasePath).ConfigureAwait(false)).Count;
    }

    internal async Task<IReadOnlyList<NotebookPageSummary>> ListPageSummariesAsync(
        string databasePath,
        int skipCount,
        int takeCount,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return (await SchemaV2TestNotebook.ListAsync(databasePath).ConfigureAwait(false))
            .Skip(skipCount)
            .Take(takeCount)
            .ToArray();
    }
}
