using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Domain.Repositories;

namespace MemoriaNote.Persistence.Repositories;

/// <summary>Identifies one supported notebook format generation.</summary>
internal enum NotebookFormatVersion
{
    Version1 = 1,
    Version2 = 2
}

/// <summary>Describes operations exposed by a versioned repository provider.</summary>
[Flags]
internal enum NotebookRepositoryCapabilities
{
    None = 0,
    OperationalRead = 1,
    OperationalWrite = 2,
    MigrationRead = 4
}

/// <summary>Describes a versioned repository provider without exposing its storage technology.</summary>
internal sealed record NotebookRepositoryDescriptor(
    NotebookFormatVersion FormatVersion,
    NotebookRepositoryCapabilities Capabilities);

/// <summary>Supplies read ports for one operational notebook format.</summary>
internal interface INotebookRepositoryProvider
{
    NotebookRepositoryDescriptor Descriptor { get; }

    INotebookMetadataReader MetadataReader { get; }

    INotebookPageReader PageReader { get; }
}

/// <summary>Supplies read and write ports for one operational notebook format.</summary>
internal interface IWritableNotebookRepositoryProvider : INotebookRepositoryProvider
{
    INotebookMetadataWriter MetadataWriter { get; }

    INotebookPageWriter PageWriter { get; }
}

/// <summary>Reads complete metadata snapshots without interpreting unknown keys.</summary>
internal interface INotebookMetadataReader
{
    Task<NotebookMetadataSnapshot> LoadAsync(
        NotebookId notebookId,
        CancellationToken cancellationToken);
}

/// <summary>Applies metadata changes while preserving unspecified and unknown keys.</summary>
internal interface INotebookMetadataWriter
{
    Task<NotebookMetadataSnapshot> UpdateAsync(
        NotebookId notebookId,
        NotebookMetadataChanges changes,
        CancellationToken cancellationToken);
}

/// <summary>Reads pages and body-free summaries from an operational notebook.</summary>
internal interface INotebookPageReader
{
    Task<RepositoryPage> FindByIdAsync(
        NotebookId notebookId,
        PageId pageId,
        CancellationToken cancellationToken);

    Task<RepositoryPage> FindByNameAsync(
        NotebookId notebookId,
        string name,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryPage>> ListByIdPrefixAsync(
        NotebookId notebookId,
        string normalizedPageIdPrefix,
        int maximumCount,
        CancellationToken cancellationToken);

    Task<int> CountAsync(
        NotebookId notebookId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RepositoryPageSummary>> ListSummariesAsync(
        NotebookId notebookId,
        int skipCount,
        int takeCount,
        CancellationToken cancellationToken);
}

/// <summary>Creates, replaces, and deletes authoritative operational page rows.</summary>
internal interface INotebookPageWriter
{
    Task<RepositoryPageWriteResult> CreateAsync(
        NotebookId notebookId,
        RepositoryPageCreateRequest request,
        CancellationToken cancellationToken);

    Task<RepositoryPageWriteResult> UpdateAsync(
        NotebookId notebookId,
        PageId pageId,
        long expectedRevision,
        RepositoryPageUpdateRequest request,
        CancellationToken cancellationToken);

    Task<RepositoryPageWriteResult> DeleteAsync(
        NotebookId notebookId,
        PageId pageId,
        long expectedRevision,
        CancellationToken cancellationToken);
}
