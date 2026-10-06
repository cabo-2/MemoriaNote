namespace MemoriaNote.Core.Tests.Infrastructure;

internal static class NotebookTestExtensions
{
    internal static Page ReadPage(this Notebook notebook, string name, int index)
    {
        return notebook.Repository.FindPageAsync(
                notebook.DatabasePath,
                name,
                index,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static Page ReadPage(this Notebook notebook, Guid guid)
    {
        return notebook.Repository.FindPageAsync(
                notebook.DatabasePath,
                guid,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static IEnumerable<Page> ReadPage(this Notebook notebook, string name)
    {
        return notebook.Repository.ListPagesByHeadingAsync(
                notebook.DatabasePath,
                name,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static Page CreatePage(
        this Notebook notebook,
        string name,
        string text,
        string? directory = null)
    {
        return notebook.Repository.CreatePageAsync(
                notebook.DatabasePath,
                name,
                text,
                directory,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static void UpdatePage(this Notebook notebook, Page page)
    {
        var persistedPage = notebook.Repository.UpdatePageAsync(
                notebook.DatabasePath,
                page,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        page.Rowid = persistedPage.Rowid;
        page.Index = persistedPage.Index;
        page.UpdateTime = persistedPage.UpdateTime;
    }

    internal static void DeletePage(this Notebook notebook, Guid guid)
    {
        notebook.Repository.DeletePageAsync(
                notebook.DatabasePath,
                guid,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static int CountPages(this Notebook notebook)
    {
        return notebook.Repository.CountPagesAsync(
                notebook.DatabasePath,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal static NotebookMetadataResult UpdateMetadata(
        this Notebook notebook,
        NotebookMetadataPatch patch)
    {
        return notebook.UpdateMetadataAsync(patch, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
