#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MemoriaNote.Domain;

namespace MemoriaNote.Integration.Validation;

/// <summary>Contains one exact logical page for deterministic corpus publication.</summary>
public sealed class DeterministicNotebookPage
{
    /// <summary>Initializes one deterministic logical page.</summary>
    public DeterministicNotebookPage(
        PageId pageId,
        string name,
        DateTimeOffset createTimeUtc,
        DateTimeOffset updateTimeUtc,
        long revision,
        IReadOnlyList<string> tags,
        string text)
    {
        PageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (createTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The creation timestamp must use UTC offset zero.", nameof(createTimeUtc));
        if (updateTimeUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The update timestamp must use UTC offset zero.", nameof(updateTimeUtc));
        if (updateTimeUtc < createTimeUtc)
            throw new ArgumentException("The update timestamp cannot precede creation.", nameof(updateTimeUtc));
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        CreateTimeUtc = createTimeUtc;
        UpdateTimeUtc = updateTimeUtc;
        Revision = revision;
        Tags = tags is null
            ? throw new ArgumentNullException(nameof(tags))
            : Array.AsReadOnly([.. tags]);
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>Gets the fixed Page ID.</summary>
    public PageId PageId { get; }

    /// <summary>Gets the page name.</summary>
    public string Name { get; }

    /// <summary>Gets the fixed UTC creation timestamp.</summary>
    public DateTimeOffset CreateTimeUtc { get; }

    /// <summary>Gets the fixed UTC update timestamp.</summary>
    public DateTimeOffset UpdateTimeUtc { get; }

    /// <summary>Gets the fixed positive revision.</summary>
    public long Revision { get; }

    /// <summary>Gets a copy of the logical tag collection.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets the page body.</summary>
    public string Text { get; }
}

/// <summary>Describes a new canonical notebook built from deterministic logical records.</summary>
public sealed class DeterministicNotebookPublicationRequest
{
    private readonly IReadOnlyDictionary<string, string?> _metadata;

    /// <summary>Initializes a deterministic notebook publication request.</summary>
    public DeterministicNotebookPublicationRequest(
        string destinationPath,
        IReadOnlyDictionary<string, string?> metadata,
        IAsyncEnumerable<DeterministicNotebookPage> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(metadata);
        DestinationPath = destinationPath;
        _metadata = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(metadata, StringComparer.Ordinal));
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    /// <summary>Gets the new destination path.</summary>
    public string DestinationPath { get; }

    /// <summary>Gets all managed and extension metadata values.</summary>
    public IReadOnlyDictionary<string, string?> Metadata => _metadata;

    /// <summary>Gets the streaming logical page input.</summary>
    public IAsyncEnumerable<DeterministicNotebookPage> Pages { get; }
}
