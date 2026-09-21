using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies the read-only cat command handler contract.</summary>
[TestFixture]
public sealed class PageCatCommandHandlerTests
{
    /// <summary>Verifies exact-name resolution writes the stored body without adding a newline.</summary>
    [Test]
    public async Task Cat_ByName_WritesOnlyTheExactBody()
    {
        var fixture = CatFixture.Create();
        var pageId = PageId.FromGuid(Guid.NewGuid());
        PageTargetRequest? request = null;
        fixture.Application.ResolvePageAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        };
        fixture.Application.ReadAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.Succeeded(CreatePage(pageId, "Roadmap", "first\nsecond")));

        var result = await fixture.Handler.ExecuteAsync(
            "workspace",
            "work.mnote",
            "Roadmap",
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Resolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.Resolver.NotebookOption, Is.EqualTo("work.mnote"));
            Assert.That(request?.NotebookId, Is.EqualTo(fixture.NotebookId));
            Assert.That(request?.Selector.Name, Is.EqualTo("Roadmap"));
            Assert.That(fixture.Output.StandardOutput, Is.EqualTo("first\nsecond"));
            Assert.That(fixture.Output.StandardError, Is.Empty);
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies a short Page ID is normalized before target resolution.</summary>
    [Test]
    public async Task Cat_ByPageIdPrefix_NormalizesTheSelector()
    {
        var fixture = CatFixture.Create();
        var pageId = PageId.FromGuid(Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"));
        PageTargetRequest? request = null;
        fixture.Application.ResolvePageAsyncHandler = (value, _) =>
        {
            request = value;
            return Task.FromResult(PageTargetResolution.Succeeded(
                new PageReference(fixture.NotebookId, pageId)));
        };
        fixture.Application.ReadAsyncHandler = (_, _) => Task.FromResult(
            PageOperationResult.Succeeded(CreatePage(pageId, "Page", string.Empty)));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            "ABCD",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(request?.Selector.PageIdPrefix, Is.EqualTo("abcd"));
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Is.Empty);
        }
    }

    /// <summary>Verifies invalid selector combinations fail before notebook resolution.</summary>
    [TestCase(null, null)]
    [TestCase("Page", "abcd")]
    [TestCase(null, "abc")]
    [TestCase(null, "not-hex")]
    [TestCase("   ", null)]
    public async Task Cat_InvalidSelector_ReturnsValidationBeforeResolution(
        string? pageName,
        string? pageId)
    {
        var fixture = CatFixture.Create();

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            pageName,
            pageId,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.ResolvePageAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.StartWith("Error: "));
        }
    }

    /// <summary>Verifies an ambiguous name is rejected without reading a page.</summary>
    [Test]
    public async Task Cat_AmbiguousName_ReturnsConflictAndSuggestsPageId()
    {
        var fixture = CatFixture.Create();
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Failed(PageTargetResolutionStatus.Conflict));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            "Duplicate",
            null,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain("--id"));
        }
    }

    /// <summary>Verifies an ambiguous Page ID prefix requests more characters.</summary>
    [Test]
    public async Task Cat_AmbiguousPageIdPrefix_ReturnsConflictWithoutReading()
    {
        var fixture = CatFixture.Create();
        fixture.Application.ResolvePageAsyncHandler = (_, _) => Task.FromResult(
            PageTargetResolution.Failed(PageTargetResolutionStatus.Conflict));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            null,
            "abcd",
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Conflict));
            Assert.That(fixture.Application.ReadAsyncCallCount, Is.Zero);
            Assert.That(fixture.Output.StandardOutput, Is.Empty);
            Assert.That(fixture.Output.StandardError, Does.Contain("more characters"));
        }
    }

    static Page CreatePage(PageId pageId, string name, string text)
    {
        var page = Page.Create(name, text);
        page.Guid = pageId.Value;
        return page;
    }

    sealed class CatFixture
    {
        CatFixture(
            NotebookId notebookId,
            StubApplicationService application,
            StubNotebookTargetSessionResolver resolver,
            RecordingOutput output,
            CatCommandHandler handler)
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

        internal CatCommandHandler Handler { get; }

        internal static CatFixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cat-{Guid.NewGuid():N}.mnote");
            var notebook = new Notebook(path);
            var notebookId = NotebookId.FromDatabasePath(path);
            var application = new StubApplicationService();
            var workspace = new Workspace("cat", new[] { notebook }, notebook);
            var resolver = new StubNotebookTargetSessionResolver(
                new ApplicationSession(workspace, application));
            var output = new RecordingOutput();
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new CatFixture(
                notebookId,
                application,
                resolver,
                output,
                new CatCommandHandler(executor, resolver, output));
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
