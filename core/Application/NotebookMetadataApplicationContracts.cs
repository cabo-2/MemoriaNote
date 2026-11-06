#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;

namespace MemoriaNote.Application;

/// <summary>Contains a complete, immutable notebook metadata snapshot.</summary>
public sealed class NotebookMetadataSnapshot
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    internal NotebookMetadataSnapshot(
        NotebookId notebookId,
        IReadOnlyDictionary<string, string?> values)
    {
        NotebookId = notebookId;
        _values = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(values, StringComparer.Ordinal));
    }

    /// <summary>Gets the notebook that owns the metadata.</summary>
    public NotebookId NotebookId { get; }

    /// <summary>Gets all metadata, including unknown extension keys and null values.</summary>
    public IReadOnlyDictionary<string, string?> Values => _values;
}

/// <summary>Describes metadata values to insert or replace in one atomic update.</summary>
public sealed class NotebookMetadataChangeSet
{
    private readonly IReadOnlyDictionary<string, string?> _changes;

    /// <summary>Initializes an atomic metadata update.</summary>
    /// <param name="changes">The keys to insert or replace and their exact values.</param>
    public NotebookMetadataChangeSet(IReadOnlyDictionary<string, string?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(changes, StringComparer.Ordinal));
    }

    /// <summary>Gets a copy of the requested metadata changes.</summary>
    public IReadOnlyDictionary<string, string?> Changes => _changes;
}

/// <summary>Provides storage-independent notebook metadata operations.</summary>
public interface INotebookMetadataApplication
{
    /// <summary>Reads the complete metadata snapshot.</summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The metadata snapshot or a classified failure.</returns>
    Task<NotebookOperationResult<NotebookMetadataSnapshot>> GetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Applies only the requested metadata changes atomically.</summary>
    /// <param name="request">The metadata changes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The resulting complete snapshot or a classified failure.</returns>
    Task<NotebookOperationResult<NotebookMetadataSnapshot>> UpdateAsync(
        NotebookMetadataChangeSet request,
        CancellationToken cancellationToken = default);
}
