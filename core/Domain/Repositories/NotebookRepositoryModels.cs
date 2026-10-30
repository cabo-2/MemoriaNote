using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote.Domain.Repositories;

/// <summary>Contains a complete metadata key-value snapshot for one notebook.</summary>
internal sealed class NotebookMetadataSnapshot
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal NotebookMetadataSnapshot(
        NotebookId notebookId,
        IReadOnlyDictionary<string, string> values)
    {
        NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
        ArgumentNullException.ThrowIfNull(values);
        _values = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    internal NotebookId NotebookId { get; }

    internal IReadOnlyDictionary<string, string> Values => _values;
}

/// <summary>Contains metadata values to insert or replace atomically.</summary>
internal sealed class NotebookMetadataChanges
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal NotebookMetadataChanges(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    internal IReadOnlyDictionary<string, string> Values => _values;
}

/// <summary>Represents one materialized current-format page.</summary>
internal sealed class RepositoryPage
{
    private readonly IReadOnlyList<string> _tags;

    internal RepositoryPage(
        NotebookId notebookId,
        PageId pageId,
        string name,
        DateTimeOffset createTimeUtc,
        DateTimeOffset updateTimeUtc,
        long revision,
        IReadOnlyList<string> tags,
        string text)
    {
        NotebookId = notebookId ?? throw new ArgumentNullException(nameof(notebookId));
        PageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        CreateTimeUtc = createTimeUtc;
        UpdateTimeUtc = updateTimeUtc;
        Revision = revision;
        ArgumentNullException.ThrowIfNull(tags);
        _tags = Array.AsReadOnly([.. tags]);
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    internal NotebookId NotebookId { get; }

    internal PageId PageId { get; }

    internal string Name { get; }

    internal DateTimeOffset CreateTimeUtc { get; }

    internal DateTimeOffset UpdateTimeUtc { get; }

    internal long Revision { get; }

    internal IReadOnlyList<string> Tags => _tags;

    internal string Text { get; }
}

/// <summary>Represents the body-free projection used by page listings.</summary>
internal sealed record RepositoryPageSummary(
    NotebookId NotebookId,
    PageId PageId,
    string Name,
    DateTimeOffset CreateTimeUtc,
    DateTimeOffset UpdateTimeUtc,
    long Revision);

/// <summary>Contains authoritative values for a newly created page.</summary>
internal sealed class RepositoryPageCreateRequest
{
    internal RepositoryPageCreateRequest(
        string name,
        string text,
        IReadOnlyList<string> tags)
    {
        Name = name;
        Text = text;
        Tags = tags;
    }

    internal string Name { get; }

    internal string Text { get; }

    internal IReadOnlyList<string> Tags { get; }
}

/// <summary>Contains the complete mutable replacement values for a page.</summary>
internal sealed class RepositoryPageUpdateRequest
{
    internal RepositoryPageUpdateRequest(
        string name,
        string text,
        IReadOnlyList<string> tags)
    {
        Name = name;
        Text = text;
        Tags = tags;
    }

    internal string Name { get; }

    internal string Text { get; }

    internal IReadOnlyList<string> Tags { get; }
}

/// <summary>Classifies a page mutation without exposing a storage exception.</summary>
internal enum RepositoryPageWriteStatus
{
    Success,
    NotFound,
    RevisionConflict,
    NameConflict
}

/// <summary>Reports the classified outcome of one page mutation.</summary>
internal sealed class RepositoryPageWriteResult
{
    private RepositoryPageWriteResult(
        RepositoryPageWriteStatus status,
        RepositoryPage page)
    {
        Status = status;
        Page = page;
    }

    internal RepositoryPageWriteStatus Status { get; }

    internal RepositoryPage Page { get; }

    internal static RepositoryPageWriteResult Succeeded(RepositoryPage page = null)
    {
        return new RepositoryPageWriteResult(RepositoryPageWriteStatus.Success, page);
    }

    internal static RepositoryPageWriteResult Failed(RepositoryPageWriteStatus status)
    {
        if (status == RepositoryPageWriteStatus.Success)
            throw new ArgumentOutOfRangeException(nameof(status));

        return new RepositoryPageWriteResult(status, null);
    }
}
