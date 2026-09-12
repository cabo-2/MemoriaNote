using System.Collections;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies the UI-independent workgroup state and invariants.
/// </summary>
[TestFixture]
public sealed class WorkgroupTests
{
    /// <summary>
    /// Verifies that note membership is copied and exposed as read-only state.
    /// </summary>
    [Test]
    public void Constructor_CopiesNotesAndExposesReadOnlyList()
    {
        var first = CreateNote("first");
        var second = CreateNote("second");
        var source = new List<Note> { first };
        var workgroup = new Workgroup("Test", source, first);

        source.Add(second);
        Action mutate = () => ((IList)workgroup.Notes).Add(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workgroup.Name, Is.EqualTo("Test"));
            Assert.That(workgroup.Notes, Is.EqualTo(new[] { first }));
            Assert.That(mutate, Throws.InstanceOf<NotSupportedException>());
        }
    }

    /// <summary>
    /// Verifies that selection resolves to the workgroup-owned object by stable note ID.
    /// </summary>
    [Test]
    public void SelectNote_SameIdObject_SelectsOwnedNote()
    {
        var owned = CreateNote("owned");
        var equivalent = new Note(owned.DataSource);
        var workgroup = new Workgroup(null, new[] { owned });

        workgroup.SelectNote(equivalent);

        Assert.That(workgroup.SelectedNote, Is.SameAs(owned));
    }

    /// <summary>
    /// Verifies that a note outside the workgroup cannot become selected.
    /// </summary>
    [Test]
    public void SelectNote_UnknownNote_IsRejectedWithoutChangingSelection()
    {
        var selected = CreateNote("selected");
        var workgroup = new Workgroup(null, new[] { selected }, selected);

        Action select = () => workgroup.SelectNote(CreateNote("unknown"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(select, Throws.ArgumentException);
            Assert.That(workgroup.SelectedNote, Is.SameAs(selected));
        }
    }

    /// <summary>
    /// Verifies that an empty workgroup has safe empty selection state.
    /// </summary>
    [Test]
    public void EmptyWorkgroup_HasNoSelectedNoteName()
    {
        var workgroup = new Workgroup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workgroup.SelectedNote, Is.Null);
            Assert.That(workgroup.SelectedNoteName, Is.Null);
        }
    }

    static Note CreateNote(string name)
    {
        return new Note(Path.Combine(
            Path.GetTempPath(),
            $"workgroup-{name}-{Guid.NewGuid():N}.db"));
    }
}
