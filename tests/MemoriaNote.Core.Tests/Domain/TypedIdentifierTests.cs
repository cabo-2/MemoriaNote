using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies the value semantics and validation rules of typed identifiers.
/// </summary>
[TestFixture]
public sealed class TypedIdentifierTests
{
    /// <summary>
    /// Verifies that relative and absolute locators for the same path produce the same note ID.
    /// </summary>
    [Test]
    public void NoteId_RelativeAndAbsolutePaths_AreEqual()
    {
        var absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            "notes",
            "typed-id.db");
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absolutePath);

        var absoluteId = NoteId.FromDataSource(absolutePath);
        var relativeId = NoteId.FromDataSource(relativePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(relativeId, Is.EqualTo(absoluteId));
            Assert.That(relativeId == absoluteId, Is.True);
            Assert.That(relativeId.GetHashCode(), Is.EqualTo(absoluteId.GetHashCode()));
            Assert.That(relativeId.ToString(), Is.EqualTo(Path.GetFullPath(absolutePath)));
        }
    }

    /// <summary>
    /// Verifies that note path comparison follows the current operating system.
    /// </summary>
    [Test]
    public void NoteId_PathCase_FollowsOperatingSystem()
    {
        var lowerCasePath = Path.Combine(Path.GetTempPath(), "memoria-note-case.db");
        var upperCasePath = lowerCasePath.ToUpperInvariant();
        var lowerCaseId = NoteId.FromDataSource(lowerCasePath);
        var upperCaseId = NoteId.FromDataSource(upperCasePath);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(lowerCaseId, Is.EqualTo(upperCaseId));
            Assert.That(lowerCaseId.GetHashCode(), Is.EqualTo(upperCaseId.GetHashCode()));
        }
        else
        {
            Assert.That(lowerCaseId, Is.Not.EqualTo(upperCaseId));
        }
    }

    /// <summary>
    /// Verifies that invalid note locators cannot become identifiers.
    /// </summary>
    [Test]
    public void NoteId_InvalidLocator_IsRejected()
    {
        Assert.That(
            CreateNoteId(null),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(
            CreateNoteId(string.Empty),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            CreateNoteId("   "),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            CreateNoteId(":memory:"),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            CreateNoteId(":MEMORY:"),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>
    /// Verifies that page IDs preserve their UUID and use the persisted UUID representation.
    /// </summary>
    [Test]
    public void PageId_GuidRoundTrip_PreservesValue()
    {
        var value = Guid.Parse("CD1C4A7B-B426-4BA3-9E44-ACAC4713A937");

        var pageId = PageId.FromGuid(value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pageId.Value, Is.EqualTo(value));
            Assert.That(pageId.ToString(), Is.EqualTo(value.ToString("D")));
            Assert.That(PageId.FromGuid(value), Is.EqualTo(pageId));
            Assert.That(PageId.FromGuid(value) == pageId, Is.True);
            Assert.That(PageId.FromGuid(value).GetHashCode(), Is.EqualTo(pageId.GetHashCode()));
        }
    }

    /// <summary>
    /// Verifies that the empty UUID cannot become a page identifier.
    /// </summary>
    [Test]
    public void PageId_EmptyGuid_IsRejected()
    {
        Assert.That(
            CreatePageId(Guid.Empty),
            Throws.TypeOf<ArgumentException>());
    }

    static Action CreateNoteId(string? dataSource)
    {
        return () => { _ = NoteId.FromDataSource(dataSource!); };
    }

    static Action CreatePageId(Guid value)
    {
        return () => { _ = PageId.FromGuid(value); };
    }
}
