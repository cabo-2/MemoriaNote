using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace MemoriaNote.Archive;

/// <summary>Classifies archive v1 and pre-v1 ZIP entry layouts without reading payloads.</summary>
internal static class ArchiveV1ContainerClassifier
{
    internal static ArchiveV1Classification Classify(
        IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        if (entries.Any(entry =>
                entry.FullName == ArchiveV1Format.ManifestEntryName))
        {
            return ArchiveV1Classification.ArchiveV1;
        }

        var metadataCount = entries.Count(entry =>
            entry.FullName == ArchiveV1Format.MetadataEntryName &&
            entry.Name == ArchiveV1Format.MetadataEntryName);
        var remaining = entries.Where(entry =>
            entry.FullName != ArchiveV1Format.MetadataEntryName);
        return metadataCount == 1 && remaining.All(IsLegacyPageEntry)
            ? ArchiveV1Classification.LegacyArchiveCandidate
            : ArchiveV1Classification.UnrecognizedZip;
    }

    private static bool IsLegacyPageEntry(ZipArchiveEntry entry)
    {
        if (entry.Name != entry.FullName ||
            !entry.FullName.EndsWith(".json", System.StringComparison.Ordinal))
        {
            return false;
        }

        var stem = entry.FullName[..^5];
        return stem.Length > 0 && stem.All(character => character is >= '0' and <= '9');
    }
}
