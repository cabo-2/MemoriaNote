using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the page delete command at the presentation boundary.</summary>
[TestFixture]
public sealed class PageDeleteCommandHandlerTests
{
    /// <summary>Verifies forced deletion uses the resolved owner-qualified target.</summary>
    [Test]
    public async Task Delete_ForcedByName_UsesResolvedPageReference()
    {
        var fixture = DeleteFixture.Create(isInteractive: false);
        var pageId = PageId.FromGuid(Guid.NewGuid());
        PageTargetRequest? request = null;
        DeletePageCommand? validatedCommand = null;
        DeletePageCommand? deletedCommand = null;
        fixture.Application.ResolvePageAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        };
        fixture.Application.ValidateDeleteAsyncHandler = (value, _) =>
        {
            validatedCommand = value;
            return Task.FromResult(PageOperationResult.Succeeded(
                CreatePage(pageId, "Meeting")));
        };
        fixture.Application.DeleteAsyncHandler = (value, _) =>
        {
            deletedCommand = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };

        var result = await fixture.Handler.ExecuteAsync(
            "workspace",
            "work.mnote",
            "Meeting",
            null,
            force: true,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Resolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.Resolver.NotebookOption, Is.EqualTo("work.mnote"));
            Assert.That(request?.Selector.Name, Is.EqualTo("Meeting"));
            Assert.That(validatedCommand?.NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(validatedCommand?.PageId, Is.EqualTo(pageId));
            Assert.That(deletedCommand?.Target, Is.EqualTo(validatedCommand?.Target));
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Does.Contain("Deleted page \"Meeting\""));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies an affirmative interactive response deletes the displayed target.</summary>
    [TestCase("y")]
    [TestCase(" YES ")]
    public async Task Delete_InteractiveConfirmation_DisplaysNameAndPageId(string response)
    {
        var fixture = DeleteFixture.Create(isInteractive: true, response);
        var pageId = PageId.FromGuid(Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"));
        fixture.SucceedResolution(pageId, "Meeting");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            "ABCD",
            force: false,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Input.ReadCount, Is.EqualTo(1));
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.StandardOutput, Does.Not.Contain("Delete page"));
            Assert.That(
                fixture.Output.StandardError,
                Does.Contain("Delete page 'Meeting'"));
            Assert.That(
                fixture.Output.StandardError,
                Does.Contain("abcdef01-2345-6789-abcd-ef0123456789"));
        }
    }

    /// <summary>Verifies a non-affirmative response leaves the target unchanged.</summary>
    [TestCase("n")]
    [TestCase("no")]
    [TestCase("")]
    [TestCase("maybe")]
    [TestCase(null)]
    public async Task Delete_NonAffirmativeResponse_CancelsWithoutMutation(string? response)
    {
        var fixture = DeleteFixture.Create(isInteractive: true, response);
        fixture.SucceedResolution(PageId.FromGuid(Guid.NewGuid()), "Keep");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Keep",
            null,
            force: false,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(
                fixture.Output.StandardOutput,
                Does.EndWith("Page deletion canceled." + Environment.NewLine));
            Assert.That(fixture.Output.StandardError, Does.Contain("Delete page 'Keep'"));
        }
    }

    /// <summary>Verifies redirected prompt output prevents an interactive wait.</summary>
    [Test]
    public async Task Delete_RedirectedStandardErrorWithoutForce_FailsWithoutReadingInput()
    {
        var fixture = DeleteFixture.Create(
            isInteractive: true,
            response: "yes",
            isOutputInteractive: false);
        fixture.SucceedResolution(PageId.FromGuid(Guid.NewGuid()), "Keep");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Keep",
            null,
            force: false,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Not.Contain("Delete page 'Keep'"));
            Assert.That(fixture.Output.StandardError, Does.Contain("--force"));
        }
    }

    /// <summary>Verifies non-interactive deletion requires the explicit force option.</summary>
    [Test]
    public async Task Delete_NonInteractiveWithoutForce_FailsWithoutMutation()
    {
        var fixture = DeleteFixture.Create(isInteractive: false);
        fixture.SucceedResolution(PageId.FromGuid(Guid.NewGuid()), "Keep");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Keep",
            null,
            force: false,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Does.Contain("--force"));
        }
    }

    /// <summary>Verifies dry-run validates and reports the target without confirmation.</summary>
    [Test]
    public async Task Delete_DryRunWithForce_DoesNotPromptOrMutate()
    {
        var fixture = DeleteFixture.Create(isInteractive: false);
        var pageId = PageId.FromGuid(Guid.NewGuid());
        fixture.SucceedResolution(pageId, "Preview");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Preview",
            null,
            force: true,
            dryRun: true,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Application.ValidateDeleteAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Does.Contain("Would delete page 'Preview'"));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies force bypasses only the prompt and not write validation.</summary>
    [Test]
    public async Task Delete_Force_ReadOnlyTargetFailsWithoutMutation()
    {
        var fixture = DeleteFixture.Create(isInteractive: false);
        var pageId = PageId.FromGuid(Guid.NewGuid());
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        fixture.Application.ValidateDeleteAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.Failed(
                PageOperationStatus.ReadOnly,
                PageErrorCode.ReadOnly));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Protected",
            null,
            force: true,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.ValidateDeleteAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Input.ReadCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Does.Contain("read-only"));
        }
    }

    /// <summary>Verifies cancellation while reading the prompt returns the cancellation contract.</summary>
    [Test]
    public async Task Delete_PromptCancellation_ReturnsCanceledWithoutMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = DeleteFixture.Create(
            isInteractive: true,
            readLineAsync: token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<string>(token);
            });
        fixture.SucceedResolution(PageId.FromGuid(Guid.NewGuid()), "Keep");

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Keep",
            null,
            force: false,
            dryRun: false,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Canceled));
            Assert.That(fixture.Input.ReadCount, Is.EqualTo(1));
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain("Delete page 'Keep'"));
            Assert.That(fixture.Output.StandardError, Does.Contain("Canceled:"));
        }
    }

    /// <summary>Verifies post-prompt deletion uses the fixed Page ID and revalidates it.</summary>
    [TestCase(
        PageOperationStatus.PageNotFound,
        PageErrorCode.PageNotFound,
        (int)CliExitCode.NotFound,
        "target page was not found")]
    [TestCase(
        PageOperationStatus.ReadOnly,
        PageErrorCode.ReadOnly,
        (int)CliExitCode.Conflict,
        "read-only")]
    public async Task Delete_TargetChangesAfterConfirmation_FailsWithoutFallback(
        PageOperationStatus status,
        PageErrorCode error,
        int expectedExitCode,
        string expectedError)
    {
        var fixture = DeleteFixture.Create(isInteractive: true, response: "yes");
        var pageId = PageId.FromGuid(Guid.NewGuid());
        fixture.SucceedResolution(pageId, "Original name");
        DeletePageCommand? deletedCommand = null;
        fixture.Application.DeleteAsyncHandler = (command, _) =>
        {
            deletedCommand = command;
            return Task.FromResult(PageOperationResult.Failed(
                status,
                error));
        };

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Original name",
            null,
            force: false,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expectedExitCode));
            Assert.That(deletedCommand?.PageId, Is.EqualTo(pageId));
            Assert.That(fixture.Application.ResolvePageAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain(expectedError));
        }
    }

    /// <summary>Verifies malformed selectors fail before notebook resolution.</summary>
    [TestCase(null, null)]
    [TestCase("Page", "abcd")]
    [TestCase(null, "abc")]
    public async Task Delete_InvalidSelector_FailsBeforeResolution(
        string? pageName,
        string? pageId)
    {
        var fixture = DeleteFixture.Create(isInteractive: false);

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            pageName,
            pageId,
            force: true,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
        }
    }

    /// <summary>Verifies ambiguous targets are rejected without validation or mutation.</summary>
    [Test]
    public async Task Delete_AmbiguousTarget_ReturnsConflictWithoutMutation()
    {
        var fixture = DeleteFixture.Create(isInteractive: false);
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Failed(PageTargetResolutionStatus.Conflict));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Duplicate",
            null,
            force: true,
            dryRun: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.ValidateDeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Application.DeleteAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardError, Does.Contain("--id"));
        }
    }

    static Page CreatePage(PageId pageId, string name)
    {
        var page = Page.Create(name, "body");
        page.Guid = pageId.Value;
        return page;
    }

    sealed class DeleteFixture
    {
        DeleteFixture(
            NotebookId notebookId,
            StubApplicationService application,
            StubNotebookTargetSessionResolver resolver,
            RecordingInput input,
            RecordingOutput output,
            DeletePageCommandHandler handler)
        {
            NotebookId = notebookId;
            Application = application;
            Resolver = resolver;
            Input = input;
            Output = output;
            Handler = handler;
        }

        internal NotebookId NotebookId { get; }

        internal StubApplicationService Application { get; }

        internal StubNotebookTargetSessionResolver Resolver { get; }

        internal RecordingInput Input { get; }

        internal RecordingOutput Output { get; }

        internal DeletePageCommandHandler Handler { get; }

        internal void SucceedResolution(PageId pageId, string name)
        {
            Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
                PageTargetResolution.Succeeded(new PageReference(NotebookId, pageId)));
            Application.ValidateDeleteAsyncHandler = (_, _) => Task.FromResult(
                PageOperationResult.Succeeded(CreatePage(pageId, name)));
        }

        internal static DeleteFixture Create(
            bool isInteractive,
            string? response = "",
            bool isOutputInteractive = true,
            Func<CancellationToken, Task<string>>? readLineAsync = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"delete-{Guid.NewGuid():N}.mnote");
            var notebook = new Notebook(path);
            var notebookId = NotebookId.FromDatabasePath(path);
            var application = new StubApplicationService();
            var workspace = new Workspace("delete", new[] { notebook }, notebook);
            var resolver = new StubNotebookTargetSessionResolver(
                new ApplicationSession(workspace, application));
            var input = new RecordingInput(isInteractive, response, readLineAsync);
            var output = new RecordingOutput();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new DeleteFixture(
                notebookId,
                application,
                resolver,
                input,
                output,
                new DeletePageCommandHandler(
                    executor,
                    resolver,
                    new CommandPrompt(input, output, isOutputInteractive),
                    output));
        }
    }

    sealed class RecordingInput : ICommandInput
    {
        readonly string? _response;
        readonly Func<CancellationToken, Task<string>>? _readLineAsync;

        internal RecordingInput(
            bool isInteractive,
            string? response,
            Func<CancellationToken, Task<string>>? readLineAsync)
        {
            IsInteractive = isInteractive;
            _response = response;
            _readLineAsync = readLineAsync;
        }

        public bool IsInteractive { get; }

        internal int ReadCount { get; private set; }

        public string? ReadLine()
        {
            ReadCount++;
            return _response;
        }

        public Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (_readLineAsync != null)
                return _readLineAsync(cancellationToken);

            return Task.FromResult(_response!);
        }

        public Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_response!);
        }
    }

    sealed class RecordingOutput : ICommandOutput
    {
        readonly StringBuilder _standardOutput = new();
        readonly StringBuilder _standardError = new();

        internal string StandardOutput => _standardOutput.ToString();

        internal string StandardError => _standardError.ToString();

        public void Write(string value) => _standardOutput.Append(value);

        public void WriteLine(string value) => _standardOutput.AppendLine(value);

        public void WriteError(string value) => _standardError.Append(value);

        public void WriteErrorLine(string value) => _standardError.AppendLine(value);

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
        }

        public void WriteWorkspaceNotebookList(
            IReadOnlyList<WorkspaceNotebookListEntry> entries,
            bool longFormat)
        {
        }
    }
}
