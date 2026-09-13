using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies immutable search request construction and validation.
/// </summary>
[TestFixture]
public sealed class SearchRequestTests
{
    /// <summary>
    /// Verifies that the renamed workspace scope retains its serialized numeric value.
    /// </summary>
    [Test]
    public void WorkspaceScope_RetainsLegacyNumericValue()
    {
        Assert.That((int)SearchRangeType.Workspace, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies that a note request can explicitly represent a missing selected note.
    /// </summary>
    [Test]
    public void ForNote_WithoutTarget_CreatesAnEmptyNoteContext()
    {
        var request = SearchRequest.ForNote(
            null!,
            SearchMethodType.Heading,
            null!,
            0,
            0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Query, Is.Empty);
            Assert.That(request.Method, Is.EqualTo(SearchMethodType.Heading));
            Assert.That(request.Scope, Is.EqualTo(SearchRangeType.Note));
            Assert.That(request.NoteIds, Is.Empty);
            Assert.That(request.Offset, Is.Zero);
            Assert.That(request.Limit, Is.Zero);
        }
    }

    /// <summary>
    /// Verifies that workspace targets retain their order and cannot be changed by the caller.
    /// </summary>
    [Test]
    public void ForWorkspace_CopiesOrderedTargets()
    {
        var first = CreateNoteId("first");
        var second = CreateNoteId("second");
        var targets = new List<NoteId> { first, second };

        var request = SearchRequest.ForWorkspace(
            "query",
            SearchMethodType.FullText,
            targets,
            2,
            3);
        targets.Reverse();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.NoteIds, Is.EqualTo(new[] { first, second }));
            Assert.That(request.Scope, Is.EqualTo(SearchRangeType.Workspace));
            Assert.That(request.Offset, Is.EqualTo(2));
            Assert.That(request.Limit, Is.EqualTo(3));
        }
    }

    /// <summary>
    /// Verifies that invalid paging is rejected before a request can reach a repository.
    /// </summary>
    /// <param name="offset">The requested offset.</param>
    /// <param name="limit">The requested limit.</param>
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    public void Factory_NegativePaging_Throws(int offset, int limit)
    {
        Action createRequest = () => SearchRequest.ForWorkspace(
            "query",
            SearchMethodType.Heading,
            Array.Empty<NoteId>(),
            offset,
            limit);

        Assert.That(createRequest, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Verifies that undefined search methods are rejected.
    /// </summary>
    [Test]
    public void Factory_UndefinedMethod_Throws()
    {
        Action createRequest = () => SearchRequest.ForNote(
            "query",
            (SearchMethodType)99,
            CreateNoteId("invalid-method"),
            0,
            10);

        Assert.That(createRequest, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// Verifies that a workspace context cannot contain an invalid note target.
    /// </summary>
    [Test]
    public void ForWorkspace_NullTarget_Throws()
    {
        Action createRequest = () => SearchRequest.ForWorkspace(
            "query",
            SearchMethodType.Heading,
            new NoteId[] { null! },
            0,
            10);

        Assert.That(createRequest, Throws.TypeOf<ArgumentException>());
    }

    static NoteId CreateNoteId(string name)
    {
        return NoteId.FromDataSource(Path.Combine(Path.GetTempPath(), $"{name}.db"));
    }
}
