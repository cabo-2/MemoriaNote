using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the page rename command at the presentation boundary.</summary>
[TestFixture]
public sealed class PageRenameCommandHandlerTests
{
    /// <summary>Verifies an exact name resolves to an owner-qualified rename command.</summary>
    [Test]
    public async Task Rename_ByName_UsesResolvedPageReference()
    {
        var fixture = RenameFixture.Create();
        var pageId = PageId.FromGuid(Guid.NewGuid());
        PageTargetRequest? request = null;
        RenamePageCommand? command = null;
        fixture.Application.ResolvePageAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        };
        fixture.Application.RenameAsyncHandler = (value, _) =>
        {
            command = value;
            return Task.FromResult(PageOperationResult.Succeeded());
        };

        var result = await fixture.Handler.ExecuteAsync(
            "workspace",
            "work.mnote",
            "Before",
            null,
            "After",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Resolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.Resolver.NotebookOption, Is.EqualTo("work.mnote"));
            Assert.That(request?.Selector.Name, Is.EqualTo("Before"));
            Assert.That(command?.NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(command?.PageId, Is.EqualTo(pageId));
            Assert.That(command?.Name, Is.EqualTo("After"));
            Assert.That(fixture.Application.RenameAsyncCallCount, Is.EqualTo(1));
            Assert.That(fixture.Output.StandardOutput, Does.Contain("renamed successfully"));
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies a Page ID selector is normalized before resolution.</summary>
    [Test]
    public async Task Rename_ByPageIdPrefix_NormalizesSelector()
    {
        var fixture = RenameFixture.Create();
        var pageId = PageId.FromGuid(Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"));
        PageTargetRequest? request = null;
        fixture.Application.ResolvePageAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        };

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            "ABCD",
            "After",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request?.Selector.PageIdPrefix, Is.EqualTo("abcd"));
            Assert.That(fixture.Application.RenameAsyncCallCount, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies malformed command input fails before notebook resolution.</summary>
    [TestCase(null, null, "After")]
    [TestCase("Before", "abcd", "After")]
    [TestCase(null, "abc", "After")]
    [TestCase("Before", null, null)]
    public async Task Rename_InvalidInput_FailsBeforeResolution(
        string? pageName,
        string? pageId,
        string? newName)
    {
        var fixture = RenameFixture.Create();

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            pageName,
            pageId,
            newName,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.ResolvePageAsyncCallCount, Is.Zero);
            Assert.That(fixture.Application.RenameAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.StartWith("Error: "));
        }
    }

    /// <summary>Verifies an ambiguous source is rejected without mutation.</summary>
    [Test]
    public async Task Rename_AmbiguousSource_ReturnsConflict()
    {
        var fixture = RenameFixture.Create();
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Failed(PageTargetResolutionStatus.Conflict));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Duplicate",
            null,
            "After",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.RenameAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain("--id"));
        }
    }

    sealed class RenameFixture
    {
        RenameFixture(
            NotebookId notebookId,
            StubApplicationService application,
            StubNotebookTargetSessionResolver resolver,
            RecordingOutput output,
            RenamePageCommandHandler handler)
        {
            NotebookId = notebookId;
            Application = application;
            Resolver = resolver;
            Output = output;
            Handler = handler;
        }

        internal NotebookId NotebookId { get; }

        internal StubApplicationService Application { get; }

        internal StubNotebookTargetSessionResolver Resolver { get; }

        internal RecordingOutput Output { get; }

        internal RenamePageCommandHandler Handler { get; }

        internal static RenameFixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"rename-{Guid.NewGuid():N}.mnote");
            var notebook = new Notebook(path);
            var notebookId = NotebookId.FromDatabasePath(path);
            var application = new StubApplicationService();
            var workspace = new Workspace("rename", new[] { notebook }, notebook);
            var resolver = new StubNotebookTargetSessionResolver(
                new ApplicationSession(workspace, application));
            var output = new RecordingOutput();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new RenameFixture(
                notebookId,
                application,
                resolver,
                output,
                new RenamePageCommandHandler(executor, resolver, output));
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

        public void WriteErrorLine(string value) => _standardError.AppendLine(value);

        public void WritePageList(IReadOnlyList<PageSummary> pages, bool longFormat)
        {
        }

        public void WriteNotebookList(
            IEnumerable<Notebook> notebooks,
            Notebook selectedNotebook)
        {
        }

        public void WriteNotebookCompletion(IEnumerable<Notebook> notebooks)
        {
        }
    }
}
