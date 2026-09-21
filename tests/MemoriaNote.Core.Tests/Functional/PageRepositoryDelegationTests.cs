using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that the legacy Notebook facade delegates page operations to its repository.
/// </summary>
[TestFixture]
public sealed class PageRepositoryDelegationTests
{
    /// <summary>
    /// Verifies that deletion passes the page UUID and note locator to the repository.
    /// </summary>
    [Test]
    public void DeletePage_DelegatesWithDataSourceAndUuid()
    {
        var repository = new RecordingNoteRepository();
        var dataSource = Path.Combine(Path.GetTempPath(), "delegated-note.db");
        var note = new Notebook(dataSource, repository);
        var page = Page.Create("Delegated", string.Empty);

        note.DeletePage(page.Guid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repository.DeleteDataSource, Is.EqualTo(dataSource));
            Assert.That(repository.DeletedPageId, Is.EqualTo(page.Guid));
        }
    }

    sealed class RecordingNoteRepository : IPageRepository
    {
        internal string? DeleteDataSource { get; private set; }

        internal Guid? DeletedPageId { get; private set; }

        public Task<Page> FindPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<Page> FindPageAsync(
            string dataSource,
            string name,
            int index,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Page>> ListPagesByHeadingAsync(
            string dataSource,
            string name,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Page>> ListPagesByIdPrefixAsync(
            string dataSource,
            string pageIdPrefix,
            int maximumCount,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<Page> CreatePageAsync(
            string dataSource,
            string name,
            string text,
            string directory,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<Page> UpdatePageAsync(
            string dataSource,
            Page page,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task DeletePageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            DeleteDataSource = dataSource;
            DeletedPageId = pageId;
            return Task.CompletedTask;
        }

        public Task<int> CountPagesAsync(string dataSource, CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<PageSummary>> ListPageSummariesAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
