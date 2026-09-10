using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies the immutable page summary contract.
/// </summary>
[TestFixture]
public sealed class PageSummaryTests
{
    /// <summary>
    /// Verifies that required ownership and page identifiers cannot be omitted.
    /// </summary>
    [Test]
    public void Constructor_MissingIdentifier_IsRejected()
    {
        var noteId = NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), "summary.db"));
        var pageId = PageId.FromGuid(Guid.NewGuid());

        Assert.That(
            CreatePageSummary(null, pageId),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(
            CreatePageSummary(noteId, null),
            Throws.TypeOf<ArgumentNullException>());
    }

    /// <summary>
    /// Verifies that modifying source tags does not change an existing summary.
    /// </summary>
    [Test]
    public void Constructor_Tags_AreDefensivelyCopied()
    {
        var tags = new Dictionary<string, string>()
        {
            ["Dir"] = "journal"
        };
        var summary = CreateSummary(
            NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), "summary.db")),
            PageId.FromGuid(Guid.NewGuid()),
            tags);

        tags["Dir"] = "changed";
        tags["Added"] = "after construction";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Tags["Dir"], Is.EqualTo("journal"));
            Assert.That(summary.Tags, Does.Not.ContainKey("Added"));
        }
    }

    /// <summary>
    /// Verifies that persistence and navigation details are absent from the application result.
    /// </summary>
    [Test]
    public void PublicProperties_ExcludePersistenceAndNavigationDetails()
    {
        var propertyNames = typeof(PageSummary)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.That(propertyNames, Does.Not.Contain("Rowid"));
        Assert.That(propertyNames, Does.Not.Contain("Uuid"));
        Assert.That(propertyNames, Does.Not.Contain("OwnerDataSource"));
        Assert.That(propertyNames, Does.Not.Contain("Parent"));
        Assert.That(propertyNames, Does.Not.Contain("Text"));
    }

    /// <summary>
    /// Verifies that projection values do not participate in an owned page's identity.
    /// </summary>
    [Test]
    public void Equality_SameOwnerAndPageIds_IgnoresProjectionValues()
    {
        var noteId = NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), "owner.db"));
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var first = CreateSummary(noteId, pageId, new Dictionary<string, string>());
        var second = new PageSummary(
            noteId,
            pageId,
            "Changed",
            99,
            new Dictionary<string, string> { ["Changed"] = "true" },
            "ChangedType",
            DateTime.MinValue,
            DateTime.MaxValue,
            true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first == second, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }
    }

    /// <summary>
    /// Verifies that the owning note is part of a page summary's identity.
    /// </summary>
    [Test]
    public void Equality_SamePageIdWithDifferentOwner_ReturnsFalse()
    {
        var pageId = PageId.FromGuid(Guid.NewGuid());
        var first = CreateSummary(
            NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), "first-owner.db")),
            pageId,
            null);
        var second = CreateSummary(
            NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), "second-owner.db")),
            pageId,
            null);
        object firstObject = first;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first != second, Is.True);
            Assert.That(first.Equals(default(PageSummary)), Is.False);
            Assert.That(firstObject.Equals("not a summary"), Is.False);
        }
    }

    static PageSummary CreateSummary(
        NoteId noteId,
        PageId pageId,
        IReadOnlyDictionary<string, string>? tags)
    {
        return new PageSummary(
            noteId,
            pageId,
            "Summary",
            1,
            tags!,
            nameof(Page),
            new DateTime(2026, 9, 10, 1, 2, 3, DateTimeKind.Utc),
            new DateTime(2026, 9, 10, 4, 5, 6, DateTimeKind.Utc),
            false);
    }

    static Action CreatePageSummary(NoteId? noteId, PageId? pageId)
    {
        return () => { _ = CreateSummary(noteId!, pageId!, null); };
    }
}
