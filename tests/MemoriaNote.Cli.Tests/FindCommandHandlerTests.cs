using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies final find request mapping, streaming, and failure behavior.</summary>
[TestFixture]
public sealed class FindCommandHandlerTests
{
    /// <summary>Verifies find maps every option and reads one session in fixed batches.</summary>
    [Test]
    public async Task Execute_WithResults_StreamsMappedRequestAndReportsHasMore()
    {
        var fixture = FindFixture.Create();
        NotebookBodySearchRequest? request = null;
        CancellationToken applicationToken = default;
        var search = new StubBodySearchSession();
        search.Enqueue(Batch(
            isCompleted: false,
            hasMore: false,
            Hit("01234567-89ab-cdef-0123-456789abcdef", "Alpha")));
        search.Enqueue(Batch(
            isCompleted: true,
            hasMore: true,
            Hit("fedcba98-7654-3210-fedc-ba9876543210", "Beta")));
        fixture.Application.StartBodySearchAsyncHandler = (value, token) =>
        {
            request = value;
            applicationToken = token;
            return Task.FromResult(
                BodySearchCommandResult<ICurrentBodySearchSession>.Succeeded(search));
        };
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Handler.ExecuteAsync(
            "workspace",
            "notes.mnote",
            new[] { "sqlite migration", "AND" },
            "updated",
            1000,
            caseSensitive: true,
            noSnippet: true,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero);
            Assert.That(fixture.Resolver.WorkspaceOption, Is.EqualTo("workspace"));
            Assert.That(fixture.Resolver.NotebookOption, Is.EqualTo("notes.mnote"));
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Query, Is.EqualTo("sqlite migration AND"));
            Assert.That(request.Sort, Is.EqualTo(NotebookBodySearchSort.Updated));
            Assert.That(request.Limit, Is.EqualTo(1000));
            Assert.That(request.IncludeSnippet, Is.False);
            Assert.That(
                request.CaseSensitivity,
                Is.EqualTo(NotebookBodySearchCaseSensitivity.Sensitive));
            Assert.That(applicationToken, Is.EqualTo(cancellation.Token));
            Assert.That(search.MaximumCounts, Is.EqualTo(new[] { 20, 20 }));
            Assert.That(search.DisposeCount, Is.EqualTo(1));
            Assert.That(
                Lines(fixture.StandardOutput),
                Is.EqualTo(new[]
                {
                    "01234567-89ab-cdef-0123-456789abcdef Alpha",
                    "fedcba98-7654-3210-fedc-ba9876543210 Beta"
                }));
            Assert.That(
                fixture.StandardError.ToString(),
                Is.EqualTo(
                    "More results are available; refine the keywords " +
                    "(maximum limit is 1000)." + Environment.NewLine));
        }
    }

    /// <summary>Verifies a terminal failure after output remains an explicit partial result.</summary>
    [Test]
    public async Task Execute_WhenReadFailsAfterOutput_ReturnsPartialDiagnostic()
    {
        var fixture = FindFixture.Create();
        var search = new StubBodySearchSession();
        search.Enqueue(Batch(
            isCompleted: false,
            hasMore: false,
            Hit("01234567-89ab-cdef-0123-456789abcdef", "Alpha")));
        search.EnqueueFailure(
            NotebookFailureCategory.UnexpectedStorage,
            "Safe storage failure.");
        fixture.Application.StartBodySearchAsyncHandler = (_, _) => Task.FromResult(
            BodySearchCommandResult<ICurrentBodySearchSession>.Succeeded(search));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            new[] { "alpha" },
            null,
            null,
            caseSensitive: false,
            noSnippet: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(
                Lines(fixture.StandardOutput),
                Is.EqualTo(new[]
                {
                    "01234567-89ab-cdef-0123-456789abcdef Alpha"
                }));
            Assert.That(
                fixture.StandardError.ToString(),
                Is.EqualTo(
                    "Search failed after 1 result(s); output is incomplete." +
                    Environment.NewLine));
            Assert.That(fixture.StandardError.ToString(), Does.Not.Contain("Partial:"));
            Assert.That(fixture.StandardError.ToString(), Does.Not.Contain("storage"));
            Assert.That(search.DisposeCount, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies cancellation after output uses the same incomplete-output contract.</summary>
    [Test]
    public async Task Execute_WhenCanceledAfterOutput_ReturnsPartialDiagnostic()
    {
        var fixture = FindFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var search = new StubBodySearchSession();
        search.Enqueue(Batch(
            isCompleted: false,
            hasMore: false,
            Hit("01234567-89ab-cdef-0123-456789abcdef", "Alpha")));
        search.Enqueue(token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<BodySearchCommandResult<BodySearchCommandBatch>>(
                token);
        });
        fixture.Application.StartBodySearchAsyncHandler = (_, _) => Task.FromResult(
            BodySearchCommandResult<ICurrentBodySearchSession>.Succeeded(search));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            new[] { "alpha" },
            null,
            null,
            caseSensitive: false,
            noSnippet: false,
            cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((int)CliExitCode.Partial));
            Assert.That(
                fixture.StandardError.ToString(),
                Is.EqualTo(
                    "Search failed after 1 result(s); output is incomplete." +
                    Environment.NewLine));
            Assert.That(search.DisposeCount, Is.EqualTo(1));
        }
    }

    /// <summary>Verifies failures before output retain their normal category.</summary>
    [TestCase(
        NotebookFailureCategory.Validation,
        (int)CliExitCode.Validation,
        "Invalid query.")]
    [TestCase(
        NotebookFailureCategory.Timeout,
        (int)CliExitCode.Storage,
        "Search timed out.")]
    [TestCase(
        NotebookFailureCategory.UnexpectedStorage,
        (int)CliExitCode.Storage,
        "Search failed.")]
    public async Task Execute_WhenStartFails_MapsFailureBeforeOutput(
        NotebookFailureCategory category,
        int expectedExitCode,
        string message)
    {
        var fixture = FindFixture.Create();
        fixture.Application.StartBodySearchAsyncHandler = (_, _) => Task.FromResult(
            BodySearchCommandResult<ICurrentBodySearchSession>.Failed(
                category,
                message));

        var result = await fixture.Handler.ExecuteAsync(
            null,
            null,
            new[] { "alpha" },
            null,
            null,
            caseSensitive: false,
            noSnippet: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expectedExitCode));
            Assert.That(fixture.StandardOutput.ToString(), Is.Empty);
            Assert.That(
                fixture.StandardError.ToString(),
                Is.EqualTo($"Error: {message}" + Environment.NewLine));
        }
    }

    /// <summary>Verifies invalid options fail before resolving a notebook.</summary>
    [Test]
    public async Task Execute_WithInvalidOptions_FailsBeforeResolution()
    {
        var fixture = FindFixture.Create();

        var missing = await fixture.Handler.ExecuteAsync(
            null,
            null,
            Array.Empty<string>(),
            null,
            null,
            caseSensitive: false,
            noSnippet: false,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing, Is.EqualTo((int)CliExitCode.Validation));
            Assert.That(fixture.Resolver.ResolveCount, Is.Zero);
            Assert.That(fixture.Application.StartBodySearchAsyncCallCount, Is.Zero);
            Assert.That(fixture.StandardError.ToString(), Does.Contain("mn ls"));
        }
    }

    static BodySearchCommandBatch Batch(
        bool isCompleted,
        bool hasMore,
        params BodySearchCommandHit[] hits) =>
        new BodySearchCommandBatch(hits, isCompleted, hasMore);

    static BodySearchCommandHit Hit(string pageId, string name) =>
        new BodySearchCommandHit(
            PageId.FromGuid(Guid.Parse(pageId)),
            name,
            null);

    static string[] Lines(StringWriter writer) => writer.ToString().Split(
        new[] { '\r', '\n' },
        StringSplitOptions.RemoveEmptyEntries);

    sealed class FindFixture
    {
        FindFixture(
            StubApplicationService application,
            StubNotebookTargetSessionResolver resolver,
            StringWriter standardOutput,
            StringWriter standardError,
            FindCommandHandler handler)
        {
            Application = application;
            Resolver = resolver;
            StandardOutput = standardOutput;
            StandardError = standardError;
            Handler = handler;
        }

        internal StubApplicationService Application { get; }

        internal StubNotebookTargetSessionResolver Resolver { get; }

        internal StringWriter StandardOutput { get; }

        internal StringWriter StandardError { get; }

        internal FindCommandHandler Handler { get; }

        internal static FindFixture Create()
        {
            var notebook = new Notebook(Path.Combine(
                Path.GetTempPath(),
                $"find-handler-{Guid.NewGuid():N}.mnote"));
            var workspace = new Workspace("find-handler", new[] { notebook }, notebook);
            var application = new StubApplicationService();
            var resolver = new StubNotebookTargetSessionResolver(
                new ApplicationSession(workspace, application));
            var standardOutput = new StringWriter();
            var standardError = new StringWriter();
            var output = new ConsoleCommandOutput(standardOutput, standardError);
            var executor = new CliCommandExecutor(
                output,
                new CliErrorMapper(),
                NullLogger<CliCommandExecutor>.Instance);
            return new FindFixture(
                application,
                resolver,
                standardOutput,
                standardError,
                new FindCommandHandler(executor, resolver, output));
        }
    }

    sealed class StubBodySearchSession : ICurrentBodySearchSession
    {
        readonly Queue<Func<CancellationToken,
            Task<BodySearchCommandResult<BodySearchCommandBatch>>>> _reads = new();

        internal List<int> MaximumCounts { get; } = new();

        internal int DisposeCount { get; private set; }

        internal void Enqueue(BodySearchCommandBatch batch)
        {
            Enqueue(_ => Task.FromResult(
                BodySearchCommandResult<BodySearchCommandBatch>.Succeeded(batch)));
        }

        internal void EnqueueFailure(
            NotebookFailureCategory category,
            string message)
        {
            Enqueue(_ => Task.FromResult(
                BodySearchCommandResult<BodySearchCommandBatch>.Failed(
                    category,
                    message)));
        }

        internal void Enqueue(Func<CancellationToken,
            Task<BodySearchCommandResult<BodySearchCommandBatch>>> read)
        {
            _reads.Enqueue(read);
        }

        public Task<BodySearchCommandResult<BodySearchCommandBatch>> ReadAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            MaximumCounts.Add(maximumCount);
            return _reads.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
