#nullable enable

using System;
using MemoriaNote.Domain;

namespace MemoriaNote.Application;

/// <summary>Describes a request to create a canonical current-format notebook.</summary>
public sealed class NotebookCreateRequest
{
    /// <summary>Initializes a notebook creation request.</summary>
    /// <param name="databasePath">The new notebook database path.</param>
    /// <param name="title">The initial notebook title. An empty title is valid.</param>
    public NotebookCreateRequest(string databasePath, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(title);
        DatabasePath = databasePath;
        Title = title;
    }

    /// <summary>Gets the new notebook database path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the initial notebook title.</summary>
    public string Title { get; }
}

/// <summary>Describes a request to recognize, validate, and open a notebook.</summary>
public sealed class NotebookOpenRequest
{
    /// <summary>Initializes a notebook open request.</summary>
    /// <param name="databasePath">The existing notebook database path.</param>
    public NotebookOpenRequest(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    /// <summary>Gets the existing notebook database path.</summary>
    public string DatabasePath { get; }
}

/// <summary>Identifies the validated notebook represented by an application session.</summary>
public sealed class NotebookDescriptor
{
    internal NotebookDescriptor(NotebookId notebookId, int formatVersion)
    {
        NotebookId = notebookId;
        FormatVersion = formatVersion;
    }

    /// <summary>Gets the typed notebook identifier.</summary>
    public NotebookId NotebookId { get; }

    /// <summary>Gets the persisted notebook format version.</summary>
    public int FormatVersion { get; }
}
