using System;
using System.Globalization;

namespace MemoriaNote.Archive;

internal static class ArchiveV1ContentModel2Format
{
    internal const int ContentModelVersion = 2;
    internal const string SourceNotebookFormatVersion = "2";

    internal static bool IsCanonicalTimestamp(string value) =>
        value is not null &&
        value.Length == 27 &&
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

    internal static bool IsCanonicalPageId(string value) =>
        value is not null &&
        Guid.TryParseExact(value, "D", out var pageId) &&
        pageId != Guid.Empty &&
        string.Equals(pageId.ToString("D"), value, StringComparison.Ordinal);
}
