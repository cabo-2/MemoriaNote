using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies error classification at the CLI presentation boundary.</summary>
[TestFixture]
[Category("Functional")]
[NonParallelizable]
public sealed class ViewModelErrorHandlingTests
{
    /// <summary>
    /// Verifies that activation logs and consumes an infrastructure failure.
    /// </summary>
    [Test]
    public async Task ActivationInfrastructureFailure_CompletesNormally()
    {
        var viewModel = new ControlledActivationViewModel(
            new IOException("Database unavailable."));

        await viewModel.ActivateHandler();
    }

    /// <summary>
    /// Verifies that activation does not consume an unexpected failure.
    /// </summary>
    [Test]
    public async Task UnexpectedActivationFailure_FaultsTheReturnedTask()
    {
        var viewModel = new ControlledActivationViewModel(
            new InvalidOperationException("Activation failed."));
        InvalidOperationException? exception = null;

        try
        {
            await viewModel.ActivateHandler();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        Assert.That(exception?.Message, Is.EqualTo("Activation failed."));
    }

    /// <summary>
    /// Verifies that page validation and management consume infrastructure failures.
    /// </summary>
    [Test]
    public async Task PageManagementInfrastructureFailure_PreservesViewModelState()
    {
        var notebook = CreateNotebook();
        var content = CreateSummary(notebook, "Existing text");
        var applicationService = new StubApplicationService
        {
            ValidateCreateAsyncHandler = (_, _) =>
                Task.FromException<PageOperationResult>(
                    new IOException("Database unavailable.")),
            ReadAsyncHandler = (_, _) =>
                Task.FromException<PageOperationResult>(
                    new IOException("Database unavailable.")),
            CreateAsyncHandler = (_, _) =>
                Task.FromException<PageOperationResult>(
                    new IOException("Database unavailable."))
        };
        var viewModel = CreateViewModel(notebook, applicationService);
        viewModel.EditingTitle = "New text";
        viewModel.EditingText = "Body";
        viewModel.ManageNotice = "Unchanged";
        viewModel.Contents = new List<PageSummary> { content };
        viewModel.ContentsCount = 1;
        viewModel.ContentsViewPageIndex = (0, 0);
        viewModel.PlaceHolder = "Unchanged";

        var canCreate = await viewModel.CanCreateTextAsync(
            viewModel.EditingTitle,
            viewModel.EditingText,
            CancellationToken.None);
        await viewModel.OpenTextHandler();
        await viewModel.CreateTextHandler();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(canCreate, Is.False);
            Assert.That(viewModel.ManageNotice, Is.EqualTo("Unchanged"));
            Assert.That(viewModel.OpenedContent, Is.Null);
            Assert.That(viewModel.PlaceHolder, Is.EqualTo("Unchanged"));
        }
    }

    /// <summary>
    /// Verifies that opening a deleted search result preserves the displayed page.
    /// </summary>
    [Test]
    public async Task OpenDeletedPage_PreservesViewModelState()
    {
        var notebook = CreateNotebook();
        var content = CreateSummary(notebook, "Deleted text");
        var applicationService = new StubApplicationService
        {
            ReadAsyncHandler = (_, _) => Task.FromResult(
                PageOperationResult.Failed(
                    PageOperationStatus.PageNotFound,
                    PageErrorCode.PageNotFound))
        };
        var viewModel = CreateViewModel(notebook, applicationService);
        viewModel.Contents = new List<PageSummary> { content };
        viewModel.ContentsCount = 1;
        viewModel.ContentsViewPageIndex = (0, 0);
        viewModel.PlaceHolder = "Unchanged";

        await viewModel.OpenTextHandler();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.OpenedContent, Is.Null);
            Assert.That(viewModel.PlaceHolder, Is.EqualTo("Unchanged"));
        }
    }

    private static MemoriaNoteViewModel CreateViewModel(
        Notebook notebook,
        IMemoriaNoteApplicationService applicationService)
    {
        return new MemoriaNoteViewModel(
            new ConfigurationCli(),
            new Workspace(null, new[] { notebook }, notebook),
            applicationService,
            NullLogger<MemoriaNoteViewModel>.Instance);
    }

    private static Notebook CreateNotebook()
    {
        return new Notebook(Path.Combine(
            Path.GetTempPath(),
            $"view-model-{Guid.NewGuid():N}.db"));
    }

    private static PageSummary CreateSummary(Notebook notebook, string name)
    {
        var now = DateTime.UtcNow;
        return new PageSummary(
            NotebookId.FromDatabasePath(notebook.DatabasePath),
            PageId.FromGuid(Guid.NewGuid()),
            name,
            1,
            new Dictionary<string, string>(),
            "text/plain",
            now,
            now,
            false);
    }

    private sealed class ControlledActivationViewModel : MemoriaNoteViewModel
    {
        private readonly Exception _exception;

        internal ControlledActivationViewModel(Exception exception)
            : base(
                new ConfigurationCli(),
                new Workspace(),
                new StubApplicationService(),
                NullLogger<MemoriaNoteViewModel>.Instance)
        {
            _exception = exception;
        }

        protected override void OnActivate()
        {
            throw _exception;
        }
    }
}
