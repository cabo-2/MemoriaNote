using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that presentation notifications remain outside the workspace model.
/// </summary>
[TestFixture]
[Category("Functional")]
public sealed class PresentationWorkspaceAdapterTests
{
    /// <summary>
    /// Verifies that selecting a note updates the workspace and notifies presentation bindings.
    /// </summary>
    [Test]
    public void SelectNotebook_UpdatesSelectionAndRaisesIndexNotification()
    {
        var first = CreateNotebook("first");
        var second = CreateNotebook("second");
        var service = new TestableService(
            new Workspace(null, new[] { first, second }, first));
        var changedProperties = new List<string?>();
        service.PropertyChanged += (_, change) =>
            changedProperties.Add(change.PropertyName);

        service.SelectNotebook(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Workspace.SelectedNotebook, Is.SameAs(second));
            Assert.That(service.SelectedNotebookIndex, Is.EqualTo(1));
            Assert.That(
                changedProperties,
                Does.Contain(nameof(MemoriaNoteService.SelectedNotebookIndex)));
            Assert.That(service.NoteNames, Has.Count.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that an invalid presentation index preserves the current selection.
    /// </summary>
    [Test]
    public void SelectNotebook_InvalidIndex_IsRejectedWithoutChangingSelection()
    {
        var note = CreateNotebook("selected");
        var service = new TestableService(
            new Workspace(null, new[] { note }, note));

        Action select = () => service.SelectNotebook(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(select, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(service.Workspace.SelectedNotebook, Is.SameAs(note));
            Assert.That(service.SelectedNotebookIndex, Is.Zero);
        }
    }

    static Notebook CreateNotebook(string name)
    {
        return new Notebook(Path.Combine(
            Path.GetTempPath(),
            $"adapter-{name}-{Guid.NewGuid():N}.db"));
    }

    sealed class TestableService : MemoriaNoteService
    {
        internal TestableService(Workspace workspace)
            : base(workspace)
        {
        }
    }
}
