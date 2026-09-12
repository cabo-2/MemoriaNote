using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies that presentation notifications remain outside the workgroup model.
/// </summary>
[TestFixture]
[Category("Functional")]
public sealed class PresentationWorkgroupAdapterTests
{
    /// <summary>
    /// Verifies that selecting a note updates the workgroup and notifies presentation bindings.
    /// </summary>
    [Test]
    public void SelectNote_UpdatesSelectionAndRaisesIndexNotification()
    {
        var first = CreateNote("first");
        var second = CreateNote("second");
        var service = new TestableService(
            new Workgroup(null, new[] { first, second }, first));
        var changedProperties = new List<string?>();
        service.PropertyChanged += (_, change) =>
            changedProperties.Add(change.PropertyName);

        service.SelectNote(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Workgroup.SelectedNote, Is.SameAs(second));
            Assert.That(service.SelectedNoteIndex, Is.EqualTo(1));
            Assert.That(
                changedProperties,
                Does.Contain(nameof(MemoriaNoteService.SelectedNoteIndex)));
            Assert.That(service.NoteNames, Has.Count.EqualTo(2));
        }
    }

    /// <summary>
    /// Verifies that an invalid presentation index preserves the current selection.
    /// </summary>
    [Test]
    public void SelectNote_InvalidIndex_IsRejectedWithoutChangingSelection()
    {
        var note = CreateNote("selected");
        var service = new TestableService(
            new Workgroup(null, new[] { note }, note));

        Action select = () => service.SelectNote(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(select, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(service.Workgroup.SelectedNote, Is.SameAs(note));
            Assert.That(service.SelectedNoteIndex, Is.Zero);
        }
    }

    static Note CreateNote(string name)
    {
        return new Note(Path.Combine(
            Path.GetTempPath(),
            $"adapter-{name}-{Guid.NewGuid():N}.db"));
    }

    sealed class TestableService : MemoriaNoteService
    {
        internal TestableService(Workgroup workgroup)
            : base(workgroup)
        {
        }
    }
}
