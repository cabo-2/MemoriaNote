using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies identity-based equality for page and compatibility content entities.
/// </summary>
[TestFixture]
public sealed class PageIdentityTests
{
    /// <summary>
    /// Verifies that mutable page state does not affect equality, hashing, or hash collection use.
    /// </summary>
    [Test]
    public void Equality_AfterMutableStateChanges_RemainsStable()
    {
        var page = Page.Create("Original", "original text", "original-dir");
        var samePage = Page.Create("Copy", "copy text");
        samePage.Guid = page.Guid;
        var originalHashCode = page.GetHashCode();
        var set = new HashSet<Page> { page };
        var dictionary = new Dictionary<Page, string> { [page] = "stored" };

        page.Rowid = 42;
        page.Name = "Changed";
        page.Index = 7;
        page.TagDict[PageTag.Dir] = "changed-dir";
        page.TagDict["Status"] = "reviewed";
        page.ContentType = "ChangedType";
        page.CreateTime = page.CreateTime.AddDays(-1);
        page.UpdateTime = page.UpdateTime.AddDays(1);
        page.IsErased = true;
        page.OwnerDataSource = "changed-owner.db";
        page.Parent = new object();
        page.Text = "changed text";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page, Is.EqualTo(samePage));
            Assert.That(page.GetHashCode(), Is.EqualTo(originalHashCode));
            Assert.That(set.Contains(page), Is.True);
            Assert.That(set.Contains(samePage), Is.True);
            Assert.That(dictionary[samePage], Is.EqualTo("stored"));
        }

        Assert.That(set.Remove(samePage), Is.True);
    }

    /// <summary>
    /// Verifies that Page and Content use one consistent identity contract.
    /// </summary>
    [Test]
    public void PageAndContent_WithSameId_AreEqualSymmetrically()
    {
        var page = Page.Create("Page", "text");
        var content = page.GetContent();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.EntityEquals(content), Is.True);
            Assert.That(content.EntityEquals(page), Is.True);
            Assert.That(page.Equals((object)content), Is.True);
            Assert.That(content.Equals((object)page), Is.True);
            Assert.That(Content.EntityEquals(page, content), Is.True);
            Assert.That(Content.Equals(page, content), Is.True);
            Assert.That(page.GetHashCode(), Is.EqualTo(content.GetHashCode()));
        }
    }

    /// <summary>
    /// Verifies null, unrelated-type, and different-ID comparisons.
    /// </summary>
    [Test]
    public void Equality_InvalidOrDifferentValue_ReturnsFalse()
    {
        var page = Page.Create("Page", "text")!;
        var differentPage = Page.Create("Page", "text");
        object pageObject = page;
        IContent pageContent = page;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Equals(default(Page)), Is.False);
            Assert.That(pageObject.Equals(null), Is.False);
            Assert.That(pageContent.EntityEquals(default(IContent)), Is.False);
            Assert.That(pageObject!.Equals("not content"), Is.False);
            Assert.That(page!.Equals(differentPage), Is.False);
            Assert.That(Content.Equals(page, differentPage), Is.False);
        }
    }

    /// <summary>
    /// Verifies that distinct transient entities without IDs are not equal or hashable.
    /// </summary>
    [Test]
    public void EmptyId_IsReflexiveButCannotRepresentSharedIdentityOrBeHashed()
    {
        var page = new Page();
        var other = new Page();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Equals(page), Is.True);
            Assert.That(page.Equals(other), Is.False);
            Assert.That(page.EntityEquals(other), Is.False);
            Assert.That(GetHashCode(page), Throws.TypeOf<InvalidOperationException>());
            Assert.That(AddToHashSet(page), Throws.TypeOf<InvalidOperationException>());
        }
    }

    /// <summary>
    /// Verifies that the standard factory assigns a non-empty page ID before returning.
    /// </summary>
    [Test]
    public void Create_AssignsIdentityBeforeReturningTransientPage()
    {
        var page = Page.Create("Page", "text");
        Action getHashCode = () => _ = page.GetHashCode();

        Assert.That(page.Guid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(getHashCode, Throws.Nothing);
    }

    static Action GetHashCode(Page page)
    {
        return () => _ = page.GetHashCode();
    }

    static Action AddToHashSet(Page page)
    {
        return () => _ = new HashSet<Page> { page };
    }
}
