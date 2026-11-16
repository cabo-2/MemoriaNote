using System.Runtime.CompilerServices;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Integration.Validation;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Verifies the revision 3 Scan corpus through the public Application surface.</summary>
[TestFixture]
[Category("Functional")]
public sealed class NotebookBodySearchConformanceTests
{
    private static readonly IReadOnlyList<ConformanceCase> Cases =
    [
        new("literal-and", "sqlite migration", ["alpha", "bravo"]),
        new("japanese-substring", "日本語", ["alpha", "bravo"]),
        new("english-substring", "lite", ["alpha", "bravo"]),
        new("no-diacritic-folding", "cafe", []),
        new("operator-literals", "AND NEAR", ["operators"])
    ];

    /// <summary>Runs the release query matrix through the provider-independent Application API.</summary>
    [Test]
    public async Task ApplicationScan_UsesRevision3ConformanceCorpus()
    {
        using var notebook = await CreateCorpusAsync();
        var beforeBytes = await File.ReadAllBytesAsync(notebook.DatabasePath);
        var beforeFiles = Directory.GetFileSystemEntries(notebook.DirectoryPath);
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;

        foreach (var testCase in Cases)
        {
            var result = await SearchAsync(
                application,
                new NotebookBodySearchRequest(
                    testCase.Query,
                    NotebookBodySearchSort.Name,
                    limit: 10));

            Assert.That(
                result.Hits.Select(hit => hit.Name),
                Is.EqualTo(testCase.Expected),
                testCase.Name);
            Assert.That(result.HasMore, Is.False, testCase.Name);
            foreach (var hit in result.Hits)
            {
                Assert.That(hit.Snippet, Is.Not.Null, testCase.Name);
                AssertSnippetInvariant(hit.Snippet!);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoreIntegrationContract.BodySearchConformanceCorpusRevision, Is.EqualTo(3));
            Assert.That(await File.ReadAllBytesAsync(notebook.DatabasePath), Is.EqualTo(beforeBytes));
            Assert.That(Directory.GetFileSystemEntries(notebook.DirectoryPath), Is.EqualTo(beforeFiles));
        }
    }

    /// <summary>Verifies ordinal case modes and normalization remain explicit.</summary>
    [Test]
    public async Task ApplicationScan_CaseModesDoNotNormalizeText()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;

        var insensitive = await SearchAsync(
            application,
            new NotebookBodySearchRequest(
                "microsoft ångström",
                NotebookBodySearchSort.Name));
        var sensitiveMiss = await SearchAsync(
            application,
            new NotebookBodySearchRequest(
                "microsoft",
                NotebookBodySearchSort.Name,
                caseSensitivity: NotebookBodySearchCaseSensitivity.Sensitive));
        var sensitiveHit = await SearchAsync(
            application,
            new NotebookBodySearchRequest(
                "Microsoft",
                NotebookBodySearchSort.Name,
                caseSensitivity: NotebookBodySearchCaseSensitivity.Sensitive));
        var normalization = await SearchAsync(
            application,
            new NotebookBodySearchRequest("résumé", NotebookBodySearchSort.Name));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(insensitive.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "case" }));
            Assert.That(sensitiveMiss.Hits, Is.Empty);
            Assert.That(sensitiveHit.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "case" }));
            Assert.That(normalization.Hits, Is.Empty);
            Assert.That(
                insensitive.Hits.Single().Snippet!.Matches.Select(
                    match => (match.Start, match.Length)),
                Is.EqualTo(new[] { (0, 9), (10, 8) }));
        }
    }

    /// <summary>Verifies one- and two-scalar literals plus limit lookahead.</summary>
    [Test]
    public async Task ApplicationScan_ShortQueriesAndLimitUseReleaseSemantics()
    {
        using var notebook = await CreateCorpusAsync();
        var opened = await NotebookApplication.OpenAsync(
            new NotebookOpenRequest(notebook.DatabasePath));
        await using var application = opened.Value!;

        var oneScalar = await SearchAsync(
            application,
            new NotebookBodySearchRequest("日", NotebookBodySearchSort.Name));
        var twoScalars = await SearchAsync(
            application,
            new NotebookBodySearchRequest("ab", NotebookBodySearchSort.Name));
        var limited = await SearchAsync(
            application,
            new NotebookBodySearchRequest("marker", NotebookBodySearchSort.Name, limit: 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                oneScalar.Hits.Select(hit => hit.Name),
                Is.EqualTo(new[] { "alpha", "bravo", "short" }));
            Assert.That(twoScalars.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "short" }));
            Assert.That(limited.Hits.Select(hit => hit.Name), Is.EqualTo(new[] { "alpha" }));
            Assert.That(limited.HasMore, Is.True);
        }
    }

    private static async Task<SearchResult> SearchAsync(
        NotebookApplicationSession application,
        NotebookBodySearchRequest request)
    {
        var started = await application.Pages.StartBodySearchAsync(request);
        Assert.That(started.IsSuccess, Is.True, started.Failure?.Code);
        await using var search = started.Value!;
        var hits = new List<NotebookBodySearchHit>();
        NotebookBodySearchBatch batch;
        do
        {
            var read = await search.ReadAsync(maximumCount: 1);
            Assert.That(read.IsSuccess, Is.True, read.Failure?.Code);
            batch = read.Value!;
            hits.AddRange(batch.Hits);
        }
        while (!batch.IsCompleted);
        return new SearchResult(hits, batch.HasMore);
    }

    private static void AssertSnippetInvariant(NotebookBodySearchSnippet snippet)
    {
        var previousEnd = 0;
        foreach (var match in snippet.Matches)
        {
            Assert.That(match.Start, Is.GreaterThanOrEqualTo(previousEnd));
            Assert.That(match.Length, Is.GreaterThan(0));
            Assert.That(match.Start + match.Length, Is.LessThanOrEqualTo(snippet.Text.Length));
            previousEnd = match.Start + match.Length;
        }
    }

    private static async Task<CorpusNotebook> CreateCorpusAsync()
    {
        var directory = new TemporaryDirectory();
        var databasePath = directory.GetPath("body-search-conformance-v3.mnote");
        var timestamp = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var pages = new[]
        {
            Page("00000000-0000-4000-8000-000000000001", "alpha", timestamp,
                "sqlite migration café 日本語 marker"),
            Page("00000000-0000-4000-8000-000000000002", "bravo", timestamp,
                "prefix sqlite migration suffix 日本語です marker marker marker"),
            Page("00000000-0000-4000-8000-000000000003", "operators", timestamp,
                "AND OR NOT NEAR \"quoted\""),
            Page("00000000-0000-4000-8000-000000000004", "short", timestamp,
                "日 ab"),
            Page("00000000-0000-4000-8000-000000000005", "case", timestamp,
                "Microsoft ÅNGSTRÖM"),
            Page("00000000-0000-4000-8000-000000000006", "normalization", timestamp,
                "re\u0301sume\u0301")
        };
        var metadata = new Dictionary<string, string?>
        {
            ["Author"] = string.Empty,
            ["CreateTime"] = null,
            ["Description"] = string.Empty,
            ["ReadOnly"] = bool.FalseString,
            ["Tag"] = string.Empty,
            ["Title"] = "Body search conformance v3"
        };
        var published = await DeterministicNotebookPublisher.PublishAsync(
            new DeterministicNotebookPublicationRequest(
                databasePath,
                metadata,
                ToAsync(pages)));
        if (!published.IsSuccess)
        {
            directory.Dispose();
            throw new InvalidOperationException(published.Failure!.Code);
        }
        return new CorpusNotebook(directory, databasePath);
    }

    private static DeterministicNotebookPage Page(
        string pageId,
        string name,
        DateTimeOffset timestamp,
        string text) => new(
            PageId.FromGuid(Guid.Parse(pageId)),
            name,
            timestamp,
            timestamp,
            revision: 1,
            [],
            text);

    private static async IAsyncEnumerable<DeterministicNotebookPage> ToAsync(
        IEnumerable<DeterministicNotebookPage> pages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
            await Task.Yield();
        }
    }

    private sealed record ConformanceCase(
        string Name,
        string Query,
        IReadOnlyList<string> Expected);

    private sealed record SearchResult(
        IReadOnlyList<NotebookBodySearchHit> Hits,
        bool HasMore);

    private sealed class CorpusNotebook : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        internal CorpusNotebook(TemporaryDirectory directory, string databasePath)
        {
            _directory = directory;
            DatabasePath = databasePath;
        }

        internal string DatabasePath { get; }

        internal string DirectoryPath => _directory.Path;

        public void Dispose() => _directory.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MemoriaNote.SearchValidation.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string GetPath(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
