using MemoriaNote.Core.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Functional;

/// <summary>
/// Verifies error classification at the application service boundary.
/// </summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ServiceErrorHandlingTests
{
    /// <summary>
    /// Verifies that activation logs and consumes an infrastructure failure.
    /// </summary>
    [Test]
    public async Task ActivationInfrastructureFailure_CompletesNormally()
    {
        var service = new ControlledActivationService(
            new IOException("Database unavailable."));

        await service.ActivateHandler();
    }

    /// <summary>
    /// Verifies that activation does not consume an unexpected failure.
    /// </summary>
    [Test]
    public async Task UnexpectedActivationFailure_FaultsTheReturnedTask()
    {
        var service = new ControlledActivationService(
            new InvalidOperationException("Activation failed."));
        InvalidOperationException? exception = null;

        try
        {
            await service.ActivateHandler();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        Assert.That(exception?.Message, Is.EqualTo("Activation failed."));
    }

    /// <summary>
    /// Verifies that text validation and management consume database failures.
    /// </summary>
    [Test]
    public void TextManagementInfrastructureFailure_PreservesServiceState()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var content = note.CreatePage("Existing text", "Existing body").GetContent();
        var workgroup = new Workgroup(null, new[] { note }, note);
        var service = new TestableService(workgroup)
        {
            EditingTitle = "New text",
            EditingText = "Body",
            ManageNotice = "Unchanged",
            Contents = new List<Content>() { content },
            ContentsCount = 1,
            ContentsViewPageIndex = (0, 0),
            PlaceHolder = "Unchanged"
        };

        SqliteConnection.ClearAllPools();
        File.Delete(database.DatabasePath);

        var canCreate = service.CanCreateText(service.EditingTitle, service.EditingText);
        service.OpenTextHandler();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(canCreate, Is.False);
            service.CreateTextHandler();
            Assert.That(service.ManageNotice, Is.EqualTo("Unchanged"));
            Assert.That(service.OpenedContent, Is.Null);
            Assert.That(service.PlaceHolder, Is.EqualTo("Unchanged"));
        }
    }

    /// <summary>
    /// Verifies that opening a deleted search result is treated as not found.
    /// </summary>
    [Test]
    public void OpenDeletedText_PreservesServiceState()
    {
        using var database = new TemporaryNoteDatabase();
        var note = database.CreateNote("test-note", "Test Note");
        var page = note.CreatePage("Deleted text", "Deleted body");
        var content = page.GetContent();
        var workgroup = new Workgroup(null, new[] { note }, note);
        var service = new TestableService(workgroup)
        {
            Contents = new List<Content>() { content },
            ContentsCount = 1,
            ContentsViewPageIndex = (0, 0),
            PlaceHolder = "Unchanged"
        };
        note.DeletePage(page);

        service.OpenTextHandler();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.OpenedContent, Is.Null);
            Assert.That(service.PlaceHolder, Is.EqualTo("Unchanged"));
        }
    }

    private sealed class ControlledActivationService : MemoriaNoteService
    {
        private readonly Exception _exception;

        internal ControlledActivationService(Exception exception)
            : base(new Workgroup())
        {
            _exception = exception;
        }

        protected override void OnActivate()
        {
            throw _exception;
        }
    }

    private sealed class TestableService : MemoriaNoteService
    {
        internal TestableService(Workgroup workgroup)
            : base(workgroup)
        {
        }
    }
}
