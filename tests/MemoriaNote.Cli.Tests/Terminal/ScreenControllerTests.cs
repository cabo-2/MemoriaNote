using MemoriaNote.Cli.Editors;
using MemoriaNote.Cli.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Terminal.Gui;

namespace MemoriaNote.Cli.Tests.Terminal;

/// <summary>Protects the existing terminal screen ordering and layout contract.</summary>
[TestFixture]
public sealed class ScreenControllerTests
{
    /// <summary>Verifies that editor requests run before the returning management screen.</summary>
    [Test]
    public void ManageAndEditorRequests_AreProcessedLastInFirstOut()
    {
        var controller = CreateController();

        controller.RequestManage();
        controller.RequestEditor();

        Assert.That(
            controller.PendingScreens,
            Is.EqualTo(new[] { typeof(EditorView), typeof(ManageView) }));
    }

    /// <summary>Verifies that requesting exit removes every pending screen.</summary>
    [Test]
    public void RequestExit_ClearsPendingScreens()
    {
        var controller = CreateController();
        controller.RequestHome();
        controller.RequestManage();

        controller.RequestExit();

        Assert.That(controller.PendingScreens, Is.Empty);
    }

    /// <summary>Verifies the layout dimensions used by both Terminal.Gui screens.</summary>
    [Test]
    public void SharedLayoutDimensions_RemainStable()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ViewHelper.NotesWidth, Is.EqualTo(42));
            Assert.That(ViewHelper.SearchTextWidth, Is.EqualTo(20));
            Assert.That(ViewHelper.NotifyOffsetWidth, Is.EqualTo(8));
            Assert.That(ViewHelper.NotifyWidth, Is.EqualTo(36));
            Assert.That(ViewHelper.ContentPosX, Is.EqualTo(4));
            Assert.That(ViewHelper.ContentWidth, Is.EqualTo(25));
            Assert.That(ViewHelper.PageUpdateTimeWidth, Is.EqualTo(40));
            Assert.That(ViewHelper.NoteTitleWidth, Is.EqualTo(40));
            Assert.That(ViewHelper.EditorPosX, Is.EqualTo(25));
        }
    }

    /// <summary>Verifies the notebook shortcut mapping used by both screens.</summary>
    [Test]
    public void NotebookShortcuts_RemainStable()
    {
        var expected = new[]
        {
            Key.D1,
            Key.D2,
            Key.D3,
            Key.D4,
            Key.D5,
            Key.D6,
            Key.D7,
            Key.D8,
            Key.D9,
            Key.D0
        };

        Assert.That(
            Enumerable.Range(0, expected.Length).Select(ViewHelper.NumberToKey),
            Is.EqualTo(expected));
    }

    static TestScreenController CreateController()
    {
        return new TestScreenController(
            new StubPageEditorWorkflow(),
            NullLoggerFactory.Instance);
    }

    sealed class TestScreenController : ScreenController
    {
        internal TestScreenController(
            IPageEditorWorkflow pageEditorWorkflow,
            NullLoggerFactory loggerFactory)
            : base(pageEditorWorkflow, loggerFactory)
        {
        }

        internal IReadOnlyList<Type> PendingScreens => _views.ToArray();
    }

    sealed class StubPageEditorWorkflow : IPageEditorWorkflow
    {
        public Task RunAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("No page editor workflow is expected.");
        }
    }
}
