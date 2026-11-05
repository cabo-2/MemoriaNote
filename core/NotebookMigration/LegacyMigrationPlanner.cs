using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.NotebookMigration;

internal sealed record MigrationPageDecision(
    long SourceRowId,
    string PageId,
    string Name,
    string CreateTime,
    string UpdateTime,
    string Tags);

internal sealed class LegacyMigrationPlan
{
    internal LegacyMigrationPlan(
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata,
        IReadOnlyDictionary<long, MigrationPageDecision> pages,
        NotebookMigrationCounts counts,
        IReadOnlyList<NotebookMigrationIssue> issues)
    {
        Metadata = metadata;
        Pages = pages;
        Counts = counts;
        Issues = issues;
    }

    internal IReadOnlyList<CurrentNotebookMetadataRecord> Metadata { get; }

    internal IReadOnlyDictionary<long, MigrationPageDecision> Pages { get; }

    internal NotebookMigrationCounts Counts { get; }

    internal IReadOnlyList<NotebookMigrationIssue> Issues { get; }

    internal CurrentNotebookTransfer CreateTransfer(
        ILegacyNotebookMigrationSource source,
        CancellationToken cancellationToken) =>
        new(Metadata, ReadMappedPagesAsync(source, cancellationToken));

    private async IAsyncEnumerable<CurrentNotebookPageRecord> ReadMappedPagesAsync(
        ILegacyNotebookMigrationSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var page in source.ReadPagesAsync(cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (!Pages.TryGetValue(page.RowId, out var decision))
                continue;

            if (!Guid.TryParseExact(page.Uuid, "D", out var pageId) ||
                !string.Equals(pageId.ToString("D"), decision.PageId, StringComparison.Ordinal))
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The legacy source changed between preflight and migration.");
            }

            yield return new CurrentNotebookPageRecord(
                decision.PageId,
                decision.Name,
                decision.CreateTime,
                decision.UpdateTime,
                1,
                decision.Tags,
                page.Text ?? string.Empty);
        }
    }
}

internal static class LegacyMigrationPlanner
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    private static readonly string[] SourceTimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.F",
        "yyyy-MM-dd HH:mm:ss.FF",
        "yyyy-MM-dd HH:mm:ss.FFF",
        "yyyy-MM-dd HH:mm:ss.FFFF",
        "yyyy-MM-dd HH:mm:ss.FFFFF",
        "yyyy-MM-dd HH:mm:ss.FFFFFF",
        TimestampFormat
    ];

    internal static async Task<LegacyMigrationPlan> CreateAsync(
        ILegacyNotebookMigrationSource source,
        MigrationResolution resolution,
        CancellationToken cancellationToken)
    {
        var metadataMapping = MapMetadata(source.Metadata);
        var metadata = metadataMapping.Metadata;
        var issues = new List<NotebookMigrationIssue>();
        var candidates = new List<Candidate>();
        var seenPageIds = new HashSet<string>(StringComparer.Ordinal);
        var seenRowIds = new HashSet<long>();
        var appliedResolutionIds = new HashSet<string>(StringComparer.Ordinal);
        var counts = new MutableCounts
        {
            SourceMetadata = source.Metadata.Count,
            MigratedMetadata = metadata.Count
        };

        await foreach (var page in source.ReadPagesAsync(cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.SourcePages++;
            counts.DroppedIndexes++;
            CountContentType(page.ContentType, counts);

            if (!seenRowIds.Add(page.RowId))
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The legacy source repeats a page row identity.");
            }

            var pageIssues = new List<NotebookMigrationIssue>();
            if (!TryPageId(page.Uuid, out var pageId))
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidPageId,
                    null,
                    page,
                    "uuid"));
            }
            else
            {
                if (!seenPageIds.Add(pageId))
                {
                    throw new LegacySourceException(
                        NotebookMigrationErrorCode.InvalidSource,
                        "The legacy source repeats a stable Page ID.");
                }
                if (!string.Equals(page.Uuid, pageId, StringComparison.Ordinal))
                    counts.CanonicalizedPageIds++;
            }

            foreach (var field in page.ReadErrors)
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidPageValue,
                    pageId,
                    page,
                    field));
            }

            if (pageId is null)
            {
                Skip(pageIssues, issues, counts);
                continue;
            }

            var hasRename = resolution.Renames.TryGetValue(pageId, out var resolvedName);
            var hasOverride = resolution.TagOverrides.TryGetValue(pageId, out var overrideLabels);
            var explicitlyExcluded = resolution.ErasedPageExclusions.Contains(pageId);
            if (hasRename || hasOverride || explicitlyExcluded)
                appliedResolutionIds.Add(pageId);

            if (page.IsErased == 1)
            {
                counts.SkippedPages++;
                issues.Add(new NotebookMigrationIssue(
                    explicitlyExcluded
                        ? NotebookMigrationIssueCode.ErasedPageExcluded
                        : NotebookMigrationIssueCode.ErasedPageSkipped,
                    explicitlyExcluded
                        ? NotebookMigrationIssueSeverity.Warning
                        : NotebookMigrationIssueSeverity.Error,
                    NotebookMigrationChangeKind.Excluded,
                    pageId,
                    page.RowId,
                    page.Index,
                    "isErased"));
                continue;
            }
            if (explicitlyExcluded)
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidResolution,
                    "The resolution excludes a page that is not erased.");
            }
            if (page.IsErased != 0)
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidPageValue,
                    pageId,
                    page,
                    "isErased"));
            }

            var name = hasRename ? resolvedName : page.Name;
            if (!SchemaV2PageValueCodec.TryValidateName(name, out var nameFailure))
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidPageName,
                    pageId,
                    page,
                    "name",
                    NameReason(nameFailure.Error),
                    nameFailure.Actual,
                    nameFailure.Limit));
            }

            string tags = null;
            try
            {
                tags = hasOverride
                    ? SchemaV2TagCodec.Encode(overrideLabels)
                    : ConvertTags(page.Tags);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidDataException or JsonException)
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidTags,
                    pageId,
                    page,
                    "tags"));
            }

            var createTime = CanonicalTimestamp(page.CreateTime, out var createChanged);
            var updateTime = CanonicalTimestamp(page.UpdateTime, out var updateChanged);
            if (createTime is null || updateTime is null)
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.InvalidTimestamp,
                    pageId,
                    page,
                    createTime is null ? "createTime" : "updateTime"));
            }
            else if (string.CompareOrdinal(updateTime, createTime) < 0)
            {
                pageIssues.Add(Error(
                    NotebookMigrationIssueCode.UpdateBeforeCreate,
                    pageId,
                    page,
                    "updateTime"));
            }

            if (pageIssues.Count > 0)
            {
                Skip(pageIssues, issues, counts);
                continue;
            }

            if (createChanged)
                counts.CanonicalizedTimestamps++;
            if (updateChanged)
                counts.CanonicalizedTimestamps++;
            if (page.Text is null)
                counts.NullTexts++;
            if (hasOverride)
            {
                counts.TagOverrides++;
                issues.Add(new NotebookMigrationIssue(
                    NotebookMigrationIssueCode.TagsOverridden,
                    NotebookMigrationIssueSeverity.Warning,
                    NotebookMigrationChangeKind.Transformed,
                    pageId,
                    page.RowId,
                    page.Index,
                    "tags"));
            }
            if (hasRename)
            {
                issues.Add(new NotebookMigrationIssue(
                    NotebookMigrationIssueCode.PageRenamed,
                    NotebookMigrationIssueSeverity.Warning,
                    NotebookMigrationChangeKind.Transformed,
                    pageId,
                    page.RowId,
                    page.Index,
                    "name",
                    name));
            }

            candidates.Add(new Candidate(
                page.RowId,
                pageId,
                name,
                page.Index,
                createTime,
                updateTime,
                tags,
                hasRename));
        }

        ValidateResolutionTargets(resolution, seenPageIds, appliedResolutionIds);
        ResolveDuplicateNames(candidates, issues, counts);

        var decisions = candidates.ToDictionary(
            candidate => candidate.SourceRowId,
            candidate => new MigrationPageDecision(
                candidate.SourceRowId,
                candidate.PageId,
                candidate.FinalName,
                candidate.CreateTime,
                candidate.UpdateTime,
                candidate.Tags));
        counts.MigratedPages = decisions.Count;
        AddAggregateIssues(issues, counts, metadataMapping.DroppedUnknownKeys);
        return new LegacyMigrationPlan(
            metadata,
            decisions,
            counts.Build(),
            issues.AsReadOnly());
    }

    private static MetadataMapping MapMetadata(
        IReadOnlyList<LegacyMetadataRecord> source)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in source)
        {
            if (row.Key is null || !values.TryAdd(row.Key, row.Value))
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The legacy metadata contains a missing or duplicate key.");
            }
        }

        if (!values.TryGetValue("Version", out var version) || version != "1" ||
            !values.ContainsKey("Name") ||
            !values.TryGetValue("Title", out var title))
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InvalidSource,
                "The legacy metadata does not satisfy the schema v1 source contract.");
        }

        var readOnly = bool.FalseString;
        if (values.TryGetValue(SchemaV2MetadataContract.ReadOnlyKey, out var readOnlyValue) &&
            readOnlyValue is not null)
        {
            if (!bool.TryParse(readOnlyValue, out var parsedReadOnly))
            {
                throw new LegacySourceException(
                    NotebookMigrationErrorCode.InvalidSource,
                    "The legacy metadata contains an invalid ReadOnly value.");
            }
            readOnly = parsedReadOnly.ToString();
        }

        var createTime = values.TryGetValue(
            SchemaV2MetadataContract.CreateTimeKey,
            out var createTimeValue)
            ? createTimeValue
            : null;
        if (createTime is not null && !SchemaV2MetadataContract.IsValidCreateTime(createTime))
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InvalidSource,
                "The legacy metadata contains an invalid CreateTime value.");
        }

        var unknownKeys = values.Keys
            .Where(key =>
                !SchemaV2MetadataContract.IsApplicationManagedKey(key) &&
                !SchemaV2MetadataContract.IsRetiredKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return new MetadataMapping(
            new[]
            {
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.AuthorKey,
                    ValueOrEmpty(values, SchemaV2MetadataContract.AuthorKey)),
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.CreateTimeKey,
                    createTime),
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.DescriptionKey,
                    ValueOrEmpty(values, SchemaV2MetadataContract.DescriptionKey)),
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.ReadOnlyKey,
                    readOnly),
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.TagKey,
                    ValueOrEmpty(values, SchemaV2MetadataContract.TagKey)),
                new CurrentNotebookMetadataRecord(
                    SchemaV2MetadataContract.TitleKey,
                    title ?? string.Empty)
            },
            Array.AsReadOnly(unknownKeys));
    }

    private static string ValueOrEmpty(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static void ResolveDuplicateNames(
        IReadOnlyList<Candidate> candidates,
        ICollection<NotebookMigrationIssue> issues,
        MutableCounts counts)
    {
        var groups = candidates
            .GroupBy(candidate => candidate.FinalName, StringComparer.Ordinal)
            .ToArray();
        var reserved = new HashSet<string>(
            groups.Select(group => group.Key),
            StringComparer.Ordinal);

        foreach (var group in groups.Where(candidate => candidate.Count() > 1))
        {
            var ordered = group
                .OrderBy(candidate => candidate.SourceIndex)
                .ThenBy(candidate => candidate.SourceRowId)
                .ThenBy(candidate => candidate.PageId, StringComparer.Ordinal)
                .ToArray();
            foreach (var candidate in ordered.Skip(1))
            {
                var number = Math.Max(2, candidate.SourceIndex);
                string generated;
                do
                {
                    generated = AppendOrdinal(group.Key, number);
                    number++;
                }
                while (!reserved.Add(generated));

                candidate.FinalName = generated;
                candidate.AutomaticRename = true;
                issues.Add(new NotebookMigrationIssue(
                    NotebookMigrationIssueCode.DuplicateNameRenamed,
                    NotebookMigrationIssueSeverity.Warning,
                    NotebookMigrationChangeKind.Transformed,
                    candidate.PageId,
                    candidate.SourceRowId,
                    candidate.SourceIndex,
                    "name",
                    generated));
            }
        }

        counts.RenamedPages = candidates.Count(candidate =>
            candidate.ExplicitRename || candidate.AutomaticRename);
    }

    private static string AppendOrdinal(string baseName, long number)
    {
        var suffix = $" ({number.ToString(CultureInfo.InvariantCulture)})";
        var allowed = SchemaV2PageValueCodec.MaximumNameScalarCount -
            suffix.EnumerateRunes().Count();
        var builder = new StringBuilder();
        foreach (var rune in baseName.EnumerateRunes().Take(allowed))
            builder.Append(rune.ToString());
        return builder.Append(suffix).ToString();
    }

    private static string ConvertTags(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "[]";

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Legacy tags must be an object.");

        var properties = new HashSet<string>(StringComparer.Ordinal);
        var labels = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!properties.Add(property.Name))
                throw new InvalidDataException("Legacy tags contain a duplicate key.");
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidDataException("Legacy tag values must be strings or null.");
            var key = EscapeTagPart(property.Name);
            labels.Add(property.Value.ValueKind == JsonValueKind.Null
                ? key
                : key + ":" + EscapeTagPart(property.Value.GetString()));
        }
        return SchemaV2TagCodec.Encode(labels);
    }

    private static string EscapeTagPart(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal);

    private static string CanonicalTimestamp(string value, out bool changed)
    {
        changed = false;
        if (value is null || !DateTime.TryParseExact(
                value,
                SourceTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        var canonical = parsed.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        changed = !string.Equals(value, canonical, StringComparison.Ordinal);
        return canonical;
    }

    private static bool TryPageId(string value, out string pageId)
    {
        pageId = null;
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
            return false;
        pageId = parsed.ToString("D");
        return true;
    }

    private static NotebookMigrationIssue Error(
        string code,
        string pageId,
        LegacyPageRecord page,
        string field,
        string reason = null,
        long? actual = null,
        long? limit = null) =>
        new(
            code,
            NotebookMigrationIssueSeverity.Error,
            NotebookMigrationChangeKind.Excluded,
            pageId,
            page.RowId,
            page.Index,
            field,
            Reason: reason,
            Actual: actual,
            Limit: limit);

    private static string NameReason(SchemaV2PageNameValidationError error) =>
        error switch
        {
            SchemaV2PageNameValidationError.EmptyOrWhitespace =>
                NotebookMigrationIssueReason.EmptyOrWhitespace,
            SchemaV2PageNameValidationError.OuterWhitespace =>
                NotebookMigrationIssueReason.OuterWhitespace,
            SchemaV2PageNameValidationError.InvalidUnicode =>
                NotebookMigrationIssueReason.InvalidUnicode,
            SchemaV2PageNameValidationError.ControlCharacter =>
                NotebookMigrationIssueReason.ControlCharacter,
            SchemaV2PageNameValidationError.ScalarLimitExceeded =>
                NotebookMigrationIssueReason.ScalarLimitExceeded,
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };

    private static void Skip(
        IEnumerable<NotebookMigrationIssue> pageIssues,
        ICollection<NotebookMigrationIssue> issues,
        MutableCounts counts)
    {
        foreach (var issue in pageIssues)
            issues.Add(issue);
        counts.SkippedPages++;
    }

    private static void ValidateResolutionTargets(
        MigrationResolution resolution,
        IReadOnlySet<string> seenPageIds,
        IReadOnlySet<string> appliedPageIds)
    {
        var requested = resolution.Renames.Keys
            .Concat(resolution.TagOverrides.Keys)
            .Concat(resolution.ErasedPageExclusions)
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Any(pageId => !seenPageIds.Contains(pageId)) ||
            requested.Any(pageId => !appliedPageIds.Contains(pageId)))
        {
            throw new LegacySourceException(
                NotebookMigrationErrorCode.InvalidResolution,
                "The resolution contains an unknown or inapplicable Page ID.");
        }
    }

    private static void CountContentType(string value, MutableCounts counts)
    {
        if (value is null)
            counts.ContentTypeNull++;
        else if (value.Length == 0)
            counts.ContentTypeEmpty++;
        else if (value == "T")
            counts.ContentTypeText++;
        else
            counts.ContentTypeOther++;
    }

    private static void AddAggregateIssues(
        ICollection<NotebookMigrationIssue> issues,
        MutableCounts counts,
        IReadOnlyList<string> droppedUnknownMetadataKeys)
    {
        if (droppedUnknownMetadataKeys.Count > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.UnknownMetadataDropped,
                NotebookMigrationIssueSeverity.Warning,
                NotebookMigrationChangeKind.Dropped,
                Field: "metadata",
                Count: droppedUnknownMetadataKeys.Count,
                MetadataKeys: droppedUnknownMetadataKeys));
        }
        if (counts.NullTexts > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.NullTextCanonicalized,
                NotebookMigrationIssueSeverity.Warning,
                NotebookMigrationChangeKind.Canonicalized,
                Field: "text",
                Count: counts.NullTexts));
        }
        if (counts.DroppedIndexes > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.IndexesDropped,
                NotebookMigrationIssueSeverity.Info,
                NotebookMigrationChangeKind.Dropped,
                Field: "index",
                Count: counts.DroppedIndexes));
        }
        if (counts.ContentTypeTotal > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.ContentTypesDropped,
                NotebookMigrationIssueSeverity.Info,
                NotebookMigrationChangeKind.Dropped,
                Field: "contentType",
                Count: counts.ContentTypeTotal));
        }
        if (counts.CanonicalizedPageIds > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.PageIdsCanonicalized,
                NotebookMigrationIssueSeverity.Info,
                NotebookMigrationChangeKind.Canonicalized,
                Field: "uuid",
                Count: counts.CanonicalizedPageIds));
        }
        if (counts.CanonicalizedTimestamps > 0)
        {
            issues.Add(new NotebookMigrationIssue(
                NotebookMigrationIssueCode.TimestampsCanonicalized,
                NotebookMigrationIssueSeverity.Info,
                NotebookMigrationChangeKind.Canonicalized,
                Field: "timestamp",
                Count: counts.CanonicalizedTimestamps));
        }
    }

    private sealed record MetadataMapping(
        IReadOnlyList<CurrentNotebookMetadataRecord> Metadata,
        IReadOnlyList<string> DroppedUnknownKeys);

    private sealed class Candidate
    {
        internal Candidate(
            long sourceRowId,
            string pageId,
            string name,
            long sourceIndex,
            string createTime,
            string updateTime,
            string tags,
            bool explicitRename)
        {
            SourceRowId = sourceRowId;
            PageId = pageId;
            FinalName = name;
            SourceIndex = sourceIndex;
            CreateTime = createTime;
            UpdateTime = updateTime;
            Tags = tags;
            ExplicitRename = explicitRename;
        }

        internal long SourceRowId { get; }
        internal string PageId { get; }
        internal string FinalName { get; set; }
        internal long SourceIndex { get; }
        internal string CreateTime { get; }
        internal string UpdateTime { get; }
        internal string Tags { get; }
        internal bool ExplicitRename { get; }
        internal bool AutomaticRename { get; set; }
    }

    private sealed class MutableCounts
    {
        internal long SourceMetadata;
        internal long MigratedMetadata;
        internal long SourcePages;
        internal long MigratedPages;
        internal long SkippedPages;
        internal long RenamedPages;
        internal long TagOverrides;
        internal long NullTexts;
        internal long CanonicalizedPageIds;
        internal long CanonicalizedTimestamps;
        internal long DroppedIndexes;
        internal long ContentTypeNull;
        internal long ContentTypeEmpty;
        internal long ContentTypeText;
        internal long ContentTypeOther;

        internal long ContentTypeTotal =>
            ContentTypeNull + ContentTypeEmpty + ContentTypeText + ContentTypeOther;

        internal NotebookMigrationCounts Build() => new(
            SourceMetadata,
            MigratedMetadata,
            SourcePages,
            MigratedPages,
            SkippedPages,
            RenamedPages,
            TagOverrides,
            NullTexts,
            CanonicalizedPageIds,
            CanonicalizedTimestamps,
            DroppedIndexes,
            new NotebookMigrationContentTypeCounts(
                ContentTypeNull,
                ContentTypeEmpty,
                ContentTypeText,
                ContentTypeOther));
    }
}
