#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();
}

internal sealed class NotebookSessionLifetime
{
    private readonly object _gate = new();
    private readonly HashSet<IAsyncDisposable> _resources = [];
    private bool _isDisposed;

    internal void ThrowIfDisposed()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_isDisposed, typeof(NotebookApplicationSession));
    }

    internal void Register(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, typeof(NotebookApplicationSession));
            _resources.Add(resource);
        }
    }

    internal void Unregister(IAsyncDisposable resource)
    {
        lock (_gate)
            _resources.Remove(resource);
    }

    internal async ValueTask DisposeAsync()
    {
        IAsyncDisposable[] resources;
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            resources = [.. _resources];
            _resources.Clear();
        }

        Exception? firstFailure = null;
        foreach (var resource in resources)
        {
            try
            {
                await resource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }
        }

        if (firstFailure is not null)
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }
}
