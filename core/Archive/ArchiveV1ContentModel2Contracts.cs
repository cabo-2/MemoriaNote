using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Archive;

/// <summary>Represents one exact content model 2 page record.</summary>
public sealed class ArchiveV1ContentModel2PageRow
{
    /// <summary>Initializes a content model 2 page record.</summary>
    public ArchiveV1ContentModel2PageRow(
        long sourceInternalId,
        string pageId,
        string name,
        string createTime,
        string updateTime,
        long revision,
        string tags,
        string text)
    {
        SourceInternalId = sourceInternalId;
        PageId = pageId;
        Name = name;
        CreateTime = createTime;
        UpdateTime = updateTime;
        Revision = revision;
        Tags = tags;
        Text = text;
    }

    /// <summary>Gets the source-local SQLite page identifier.</summary>
    public long SourceInternalId { get; }

    /// <summary>Gets the stable public Page ID.</summary>
    public string PageId { get; }

    /// <summary>Gets the page name.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical creation timestamp.</summary>
    public string CreateTime { get; }

    /// <summary>Gets the canonical update timestamp.</summary>
    public string UpdateTime { get; }

    /// <summary>Gets the optimistic-concurrency revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the canonical tags JSON text.</summary>
    public string Tags { get; }

    /// <summary>Gets the page body.</summary>
    public string Text { get; }
}

/// <summary>Supplies content model 2 records and archive creation information.</summary>
public sealed class ArchiveV1ContentModel2WriteRequest
{
    /// <summary>Initializes a content model 2 archive write request.</summary>
    public ArchiveV1ContentModel2WriteRequest(
        ArchiveV1Creator creator,
        DateTimeOffset createdAtUtc,
        IEnumerable<ArchiveV1MetadataRow> metadata,
        IAsyncEnumerable<ArchiveV1ContentModel2PageRow> pages)
    {
        Creator = creator ?? throw new ArgumentNullException(nameof(creator));
        CreatedAtUtc = createdAtUtc;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    /// <summary>Gets the archive creator.</summary>
    public ArchiveV1Creator Creator { get; }

    /// <summary>Gets the archive creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the exact metadata rows.</summary>
    public IEnumerable<ArchiveV1MetadataRow> Metadata { get; }

    /// <summary>Gets pages in ascending source-local identifier order.</summary>
    public IAsyncEnumerable<ArchiveV1ContentModel2PageRow> Pages { get; }
}

/// <summary>Receives validated content model 2 records.</summary>
public interface IArchiveV1ContentModel2RecordSink
{
    /// <summary>Receives one validated metadata row.</summary>
    ValueTask WriteMetadataAsync(
        ArchiveV1MetadataRow row,
        CancellationToken cancellationToken);

    /// <summary>Receives one validated page row.</summary>
    ValueTask WritePageAsync(
        ArchiveV1ContentModel2PageRow row,
        CancellationToken cancellationToken);
}
