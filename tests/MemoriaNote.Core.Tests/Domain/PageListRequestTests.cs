using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies immutable page-list request invariants.</summary>
[TestFixture]
public sealed class PageListRequestTests
{
    /// <summary>Verifies an omitted limit requests all pages.</summary>
    [Test]
    public void Constructor_WithoutLimit_StoresExplicitOwner()
    {
        var notebookId = NotebookId.FromDatabasePath(
            Path.Combine(Path.GetTempPath(), "page-list.mnote"));

        var request = new PageListRequest(notebookId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.NotebookId, Is.SameAs(notebookId));
            Assert.That(request.Limit, Is.Null);
        }
    }

    /// <summary>Verifies list limits must be positive when supplied.</summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_WithNonPositiveLimit_Throws(int limit)
    {
        var notebookId = NotebookId.FromDatabasePath(
            Path.Combine(Path.GetTempPath(), "page-list.mnote"));
        Action create = () => new PageListRequest(notebookId, limit);

        Assert.That(create, Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
