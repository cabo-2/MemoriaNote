#nullable enable

using System;
using System.Threading.Tasks;
using MemoriaNote.Persistence.Repositories;

namespace MemoriaNote.Application;

/// <summary>Owns the storage-independent operations for one validated notebook.</summary>
public sealed class NotebookApplicationSession : IAsyncDisposable
{
    private readonly NotebookSessionLifetime _lifetime = new();

    internal NotebookApplicationSession(
        NotebookDescriptor notebook,
        IWritableNotebookRepositoryProvider provider)
    {
        Notebook = notebook;
        Metadata = new NotebookMetadataApplicationAdapter(
            notebook.NotebookId,
            provider.MetadataReader,
            provider.MetadataWriter,
            _lifetime);
        Pages = new NotebookPageApplicationAdapter(
            notebook.NotebookId,
            provider.MetadataReader,
            provider.PageReader,
            provider.BodySearchReader,
            provider.PageWriter,
            _lifetime);
    }

    /// <summary>Gets the validated notebook descriptor.</summary>
    public NotebookDescriptor Notebook { get; }

    /// <summary>Gets notebook metadata operations.</summary>
    public INotebookMetadataApplication Metadata { get; }

    /// <summary>Gets notebook page operations.</summary>
    public INotebookPageApplication Pages { get; }

    internal NotebookSessionLifetime Lifetime => _lifetime;

    /// <summary>Ends the logical session and prevents further operations.</summary>
    /// <returns>A completed asynchronous disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class NotebookSessionLifetime
{
    private bool _isDisposed;

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, typeof(NotebookApplicationSession));
    }

    internal void Dispose() => _isDisposed = true;
}
