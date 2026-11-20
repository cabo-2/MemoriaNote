using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;

namespace MemoriaNote.Cli
{
    internal sealed class BodySearchCommandMatch
    {
        internal BodySearchCommandMatch(int start, int length)
        {
            Start = start;
            Length = length;
        }

        internal int Start { get; }

        internal int Length { get; }
    }

    internal sealed class BodySearchCommandSnippet
    {
        internal BodySearchCommandSnippet(
            string text,
            IReadOnlyList<BodySearchCommandMatch> matches)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
        }

        internal string Text { get; }

        internal IReadOnlyList<BodySearchCommandMatch> Matches { get; }
    }

    internal sealed class BodySearchCommandHit
    {
        internal BodySearchCommandHit(
            PageId pageId,
            string name,
            BodySearchCommandSnippet snippet)
        {
            PageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Snippet = snippet;
        }

        internal PageId PageId { get; }

        internal string Name { get; }

        internal BodySearchCommandSnippet Snippet { get; }
    }

    internal static class BodySearchCommandParser
    {
        internal static bool TryParseSort(
            string value,
            out NotebookBodySearchSort sort)
        {
            switch (value)
            {
                case null:
                case "name":
                    sort = NotebookBodySearchSort.Name;
                    return true;
                case "updated":
                    sort = NotebookBodySearchSort.Updated;
                    return true;
                default:
                    sort = default;
                    return false;
            }
        }
    }

    internal sealed class FindCommandHandler
    {
        const int BatchSize = 20;

        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly ICommandOutput _output;

        internal FindCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            IReadOnlyList<string> keywords,
            string sort,
            int? limit,
            bool caseSensitive,
            bool noSnippet,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (keywords == null || keywords.Count == 0)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "At least one keyword is required. Use 'mn ls' to list pages " +
                        "or match page names.");
                }
                if (limit is <= 0 or > NotebookBodySearchRequest.MaximumLimit)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        $"Limit must be between 1 and " +
                        $"{NotebookBodySearchRequest.MaximumLimit}.");
                }
                if (!BodySearchCommandParser.TryParseSort(sort, out var parsedSort))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Sort must be name or updated.");
                }

                await using var resolved = await _targetResolver.ResolveAsync(
                    workspaceOption,
                    notebookOption,
                    token);

                var effectiveLimit = limit ?? NotebookBodySearchRequest.DefaultLimit;
                var request = new NotebookBodySearchRequest(
                    string.Join(" ", keywords),
                    parsedSort,
                    effectiveLimit,
                    includeSnippet: !noSnippet,
                    caseSensitivity: caseSensitive
                        ? NotebookBodySearchCaseSensitivity.Sensitive
                        : NotebookBodySearchCaseSensitivity.Insensitive);
                var started = await resolved.Pages.StartBodySearchAsync(request, token)
                    .ConfigureAwait(false);
                if (!started.IsSuccess)
                    return MapFailure(started.Failure);

                await using var search = started.Value;
                var displayedCount = 0;
                try
                {
                    while (true)
                    {
                        var read = await search.ReadAsync(BatchSize, token)
                            .ConfigureAwait(false);
                        if (!read.IsSuccess)
                        {
                            return displayedCount == 0
                                ? MapFailure(read.Failure)
                                : PartialFailure(displayedCount);
                        }

                        var batch = read.Value;
                        _output.WriteBodySearchHits(batch.Hits.Select(Map).ToArray());
                        displayedCount += batch.Hits.Count;
                        if (!batch.IsCompleted)
                            continue;

                        if (batch.HasMore)
                            WriteHasMoreGuidance(effectiveLimit);
                        return CliCommandResult.Success();
                    }
                }
                catch (OperationCanceledException) when (displayedCount > 0)
                {
                    return PartialFailure(displayedCount);
                }
            }, cancellationToken);
        }

        CliCommandResult PartialFailure(int displayedCount)
        {
            var message =
                $"Search failed after {displayedCount} result(s); output is incomplete.";
            _output.WriteErrorLine(message);
            return CliCommandResult.Failure(
                CliErrorKind.Partial,
                message,
                alreadyReported: true);
        }

        void WriteHasMoreGuidance(int limit)
        {
            if (limit < NotebookBodySearchRequest.MaximumLimit)
            {
                _output.WriteErrorLine(
                    "More results are available; increase --limit to show more " +
                    $"(maximum {NotebookBodySearchRequest.MaximumLimit}).");
                return;
            }

            _output.WriteErrorLine(
                "More results are available; refine the keywords " +
                $"(maximum limit is {NotebookBodySearchRequest.MaximumLimit}).");
        }

        static BodySearchCommandHit Map(NotebookBodySearchHit hit) =>
            new BodySearchCommandHit(
                hit.PageId,
                hit.Name,
                hit.Snippet == null
                    ? null
                    : new BodySearchCommandSnippet(
                        hit.Snippet.Text,
                        hit.Snippet.Matches.Select(match =>
                            new BodySearchCommandMatch(
                                match.Start,
                                match.Length)).ToArray()));

        static CliCommandResult MapFailure(NotebookFailure failure)
        {
            var errorKind = failure.Category switch
            {
                NotebookFailureCategory.Validation => CliErrorKind.Validation,
                NotebookFailureCategory.NotFound => CliErrorKind.NotFound,
                NotebookFailureCategory.Conflict => CliErrorKind.Conflict,
                NotebookFailureCategory.Format => CliErrorKind.Validation,
                NotebookFailureCategory.DerivedData => CliErrorKind.Validation,
                NotebookFailureCategory.AccessOrBusy => CliErrorKind.Storage,
                NotebookFailureCategory.UnexpectedStorage => CliErrorKind.Storage,
                NotebookFailureCategory.Timeout => CliErrorKind.Storage,
                _ => CliErrorKind.Unexpected
            };
            return CliCommandResult.Failure(errorKind, failure.Message);
        }
    }
}
