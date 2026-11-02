using System;
using System.Collections.Generic;

namespace MemoriaNote.Application;

/// <summary>Represents one exact authoritative metadata value during notebook transfer.</summary>
internal sealed record CurrentNotebookMetadataRecord(string Key, string Value);

/// <summary>Represents one exact authoritative page during notebook transfer.</summary>
internal sealed record CurrentNotebookPageRecord(
    string PageId,
    string Name,
    string CreateTime,
    string UpdateTime,
    long Revision,
    string Tags,
    string Text);

/// <summary>Supplies validated current-format records to a format-independent sink.</summary>
internal sealed class CurrentNotebookTransfer
{
    internal CurrentNotebookTransfer(
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata,
        IAsyncEnumerable<CurrentNotebookPageRecord> pages)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    internal IReadOnlyList<CurrentNotebookMetadataRecord> Metadata { get; }

    internal IAsyncEnumerable<CurrentNotebookPageRecord> Pages { get; }
}
