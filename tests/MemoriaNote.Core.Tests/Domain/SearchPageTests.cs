using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies immutable search page behavior.
/// </summary>
[TestFixture]
public sealed class SearchPageTests
{
    /// <summary>
    /// Verifies that result items are copied and paging metadata is retained.
    /// </summary>
    [Test]
    public void Constructor_CopiesItemsAndRetainsPaging()
    {
        var first = CreateSummary("first");
        var second = CreateSummary("second");
        var items = new List<PageSummary> { first };

        var page = new SearchPage(items, 5, 2, 1);
        items.Add(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Is.EqualTo(new[] { first }));
            Assert.That(page.TotalCount, Is.EqualTo(5));
            Assert.That(page.Offset, Is.EqualTo(2));
            Assert.That(page.Limit, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Verifies validation of result counts and paging values.
    /// </summary>
    /// <param name="totalCount">The total result count.</param>
    /// <param name="offset">The requested offset.</param>
    /// <param name="limit">The requested limit.</param>
    [TestCase(-1, 0, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(0, 0, -1)]
    public void Constructor_NegativeValue_Throws(int totalCount, int offset, int limit)
    {
        Action createPage = () => new SearchPage(
            Array.Empty<PageSummary>(),
            totalCount,
            offset,
            limit);

        Assert.That(createPage, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Verifies that every page item must carry a valid summary identity.
    /// </summary>
    [Test]
    public void Constructor_NullItem_Throws()
    {
        Action createPage = () => new SearchPage(
            new PageSummary[] { null! },
            1,
            0,
            1);

        Assert.That(createPage, Throws.TypeOf<ArgumentException>());
    }

    static PageSummary CreateSummary(string name)
    {
        return new PageSummary(
            NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), $"{name}.db")),
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            nameof(Content),
            DateTime.UtcNow,
            DateTime.UtcNow,
            false);
    }
}
