using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Cli.Tests.Infrastructure;

/// <summary>
/// Provides the small legacy-shaped test surface needed while CLI integration tests move to the
/// schema-v2 application API.
/// </summary>
internal sealed class SchemaV2TestPageRepository
{
    internal async Task<Page> CreatePageAsync(
        string databasePath,
        string heading,
        string body,
        string? directory,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tags = string.IsNullOrEmpty(directory) ? Array.Empty<string>() : [directory];
        return ToLegacyPage(await SchemaV2TestNotebook.CreatePageAsync(
                databasePath,
                heading,
                body,
                tags)
            .ConfigureAwait(false));
    }

    internal async Task<Page?> FindPageAsync(
        string databasePath,
        Guid pageId,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = await SchemaV2TestNotebook.FindByIdAsync(
                databasePath,
                PageId.FromGuid(pageId))
            .ConfigureAwait(false);
        return page == null ? null : ToLegacyPage(page);
    }

    internal async Task<Page?> FindPageAsync(
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
        return page == null ? null : ToLegacyPage(page);
    }

    internal async Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
        string databasePath,
        string heading,
        CancellationToken token)
    {
        var page = await FindPageAsync(databasePath, heading, 1, token)
            .ConfigureAwait(false);
        return page == null ? Array.Empty<Page>() : [page];
    }

    internal async Task<int> CountPagesAsync(
        string databasePath,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return (await SchemaV2TestNotebook.ListAsync(databasePath).ConfigureAwait(false)).Count;
    }

    internal async Task<IReadOnlyList<PageSummary>> ListPageSummariesAsync(
        string databasePath,
        int skipCount,
        int takeCount,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        return (await SchemaV2TestNotebook.ListAsync(databasePath).ConfigureAwait(false))
            .Skip(skipCount)
            .Take(takeCount)
            .Select(page => new PageSummary(
                notebookId,
                page.PageId,
                page.Name,
                1,
                new Dictionary<string, string>(),
                "T",
                page.CreateTimeUtc.UtcDateTime,
                page.UpdateTimeUtc.UtcDateTime,
                false))
            .ToArray();
    }

    private static Page ToLegacyPage(NotebookPage page)
    {
        return new Page
        {
            Guid = page.PageId.Value,
            Name = page.Name,
            Index = 1,
            ContentType = "T",
            CreateTime = page.CreateTimeUtc.UtcDateTime,
            UpdateTime = page.UpdateTimeUtc.UtcDateTime,
            IsErased = false,
            Text = page.Text,
            TagDict = page.Tags.ToDictionary(tag => tag, tag => tag)
        };
    }
}
