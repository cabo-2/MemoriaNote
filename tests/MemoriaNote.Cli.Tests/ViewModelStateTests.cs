using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies that presentation notifications remain outside the workspace model.
/// </summary>
[TestFixture]
[Category("Functional")]
public sealed class ViewModelStateTests
{
    /// <summary>
    /// Verifies that selecting a note updates the workspace and notifies presentation bindings.
    /// </summary>
    [Test]
    public void SelectNotebook_UpdatesSelectionAndRaisesIndexNotification()
    {
        var first = CreateNotebook("first");
        var second = CreateNotebook("second");
        var service = CreateViewModel(
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
                Does.Contain(nameof(MemoriaNoteViewModel.SelectedNotebookIndex)));
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
        var service = CreateViewModel(
            new Workspace(null, new[] { note }, note));

        Action select = () => service.SelectNotebook(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(select, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(service.Workspace.SelectedNotebook, Is.SameAs(note));
            Assert.That(service.SelectedNotebookIndex, Is.Zero);
        }
    }

    /// <summary>
    /// Verifies that derived search labels notify their own properties.
    /// </summary>
    [Test]
    public void SearchOptions_NotifyTheirDerivedDisplayProperties()
    {
        var service = CreateViewModel(new Workspace());
        var changedProperties = new List<string?>();
        service.PropertyChanged += (_, change) =>
            changedProperties.Add(change.PropertyName);

        service.SearchRange = SearchRangeType.Workspace;
        service.SearchMethod = SearchMethodType.FullText;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.SearchRangeString, Is.EqualTo("All notes"));
            Assert.That(service.SearchMethodString, Is.EqualTo("Full text"));
            Assert.That(
                changedProperties,
                Does.Contain(nameof(MemoriaNoteViewModel.SearchRangeString)));
            Assert.That(
                changedProperties,
                Does.Contain(nameof(MemoriaNoteViewModel.SearchMethodString)));
        }
    }

    static Notebook CreateNotebook(string name)
    {
        return new Notebook(Path.Combine(
            Path.GetTempPath(),
            $"adapter-{name}-{Guid.NewGuid():N}.db"));
    }

    static MemoriaNoteViewModel CreateViewModel(Workspace workspace)
    {
        return new MemoriaNoteViewModel(
            new ConfigurationCli(),
            workspace,
            new StubApplicationService(),
            NullLogger<MemoriaNoteViewModel>.Instance);
    }
}
