using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that the legacy Note facade delegates page operations to its repository.
/// </summary>
[TestFixture]
public sealed class NoteRepositoryDelegationTests
{
    /// <summary>
    /// Verifies that deletion passes the page UUID and note locator to the repository.
    /// </summary>
    [Test]
    public void DeletePage_DelegatesWithDataSourceAndUuid()
    {
        var repository = new RecordingNoteRepository();
        var dataSource = Path.Combine(Path.GetTempPath(), "delegated-note.db");
        var note = new Note(dataSource, repository);
        var content = Content.Create<Content>("Delegated");
        content.Rowid = 42;

        note.DeletePage(content);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repository.DeleteDataSource, Is.EqualTo(dataSource));
            Assert.That(repository.DeletedPageId, Is.EqualTo(content.Guid));
        }
    }

    sealed class RecordingNoteRepository : INoteRepository
    {
        internal string? DeleteDataSource { get; private set; }

        internal Guid? DeletedPageId { get; private set; }

        public Task<Page> ReadPageAsync(
            string dataSource,
            Guid pageId,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<Page> ReadPageAsync(
            string dataSource,
            string name,
            int index,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Page>> ReadPagesAsync(
            string dataSource,
            string name,
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

        public Task<int> CountAsync(string dataSource, CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Content>> ReadContentsAsync(
            string dataSource,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
