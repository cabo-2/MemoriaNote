using System.Collections;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>
/// Verifies the UI-independent workspace state and invariants.
/// </summary>
[TestFixture]
public sealed class WorkspaceTests
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
        var workspace = new Workspace("Test", source, first);

        source.Add(second);
        Action mutate = () => ((IList)workspace.Notebooks).Add(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.Name, Is.EqualTo("Test"));
            Assert.That(workspace.Notebooks, Is.EqualTo(new[] { first }));
            Assert.That(mutate, Throws.InstanceOf<NotSupportedException>());
        }
    }

    /// <summary>
    /// Verifies that selection resolves to the workspace-owned object by stable note ID.
    /// </summary>
    [Test]
    public void SelectNotebook_SameIdObject_SelectsOwnedNote()
    {
        var owned = CreateNote("owned");
        var equivalent = new Note(owned.DataSource);
        var workspace = new Workspace(null, new[] { owned });

        workspace.SelectNotebook(equivalent);

        Assert.That(workspace.SelectedNotebook, Is.SameAs(owned));
    }

    /// <summary>
    /// Verifies that a note outside the workspace cannot become selected.
    /// </summary>
    [Test]
    public void SelectNotebook_UnknownNote_IsRejectedWithoutChangingSelection()
    {
        var selected = CreateNote("selected");
        var workspace = new Workspace(null, new[] { selected }, selected);

        Action select = () => workspace.SelectNotebook(CreateNote("unknown"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(select, Throws.ArgumentException);
            Assert.That(workspace.SelectedNotebook, Is.SameAs(selected));
        }
    }

    /// <summary>
    /// Verifies that an empty workspace has safe empty selection state.
    /// </summary>
    [Test]
    public void EmptyWorkspace_HasNoSelectedNotebookName()
    {
        var workspace = new Workspace();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.SelectedNotebook, Is.Null);
            Assert.That(workspace.SelectedNotebookName, Is.Null);
        }
    }

    static Note CreateNote(string name)
    {
        return new Note(Path.Combine(
            Path.GetTempPath(),
            $"workspace-{name}-{Guid.NewGuid():N}.db"));
    }
}
