using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MemoriaNote.Persistence.SchemaV2;

namespace MemoriaNote.Archive;

internal static class ArchiveV1ContentModel2RecordValidator
{
    internal static List<ArchiveV1ValidationIssue> ValidateMetadata(
        IReadOnlyList<ArchiveV1MetadataRow> rows)
    {
        var issues = new List<ArchiveV1ValidationIssue>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var location = $"metadata.json[{index}]";
            if (row is null || row.Key is null)
            {
                Invalid(issues, location, "an object with a string key");
                continue;
            }

            if (ArchiveV1Format.UnicodeScalarCount(row.Key) > 1024 ||
                Encoding.UTF8.GetByteCount(row.Key) > 4096)
            {
                Invalid(issues, location + ".key", "at most 1024 Unicode scalars and 4096 UTF-8 bytes");
            }

            if (!keys.Add(row.Key))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.DuplicateMetadataKey,
                    location + ".key"));
            }
        }

        foreach (var required in new[] { "Name", "Title", "Version" })
        {
            var matches = rows.Where(row => row is not null && row.Key == required).ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MissingMetadataKey,
                    "metadata.json",
                    required));
            }
            else if (matches[0].Value is null)
            {
                Invalid(issues, $"metadata.json[{required}].value", "non-null string");
            }
        }

        var version = rows.FirstOrDefault(row => row is not null && row.Key == "Version");
        if (version is not null && version.Value != ArchiveV1ContentModel2Format.SourceNotebookFormatVersion)
        {
            issues.Add(new ArchiveV1ValidationIssue(
                ArchiveV1IssueCode.MetadataVersionMismatch,
                "metadata.json[Version].value",
                ArchiveV1ContentModel2Format.SourceNotebookFormatVersion,
                version.Value ?? "null"));
        }

        return issues;
    }

    internal static List<ArchiveV1ValidationIssue> ValidatePage(
        ArchiveV1ContentModel2PageRow row,
        string location)
    {
        var issues = new List<ArchiveV1ValidationIssue>();
        if (row is null)
        {
            Invalid(issues, location, "object");
            return issues;
        }

        if (row.SourceInternalId <= 0)
            Invalid(issues, location + ".sourceInternalId", "positive signed 64-bit integer");
        if (!ArchiveV1ContentModel2Format.IsCanonicalPageId(row.PageId))
            Invalid(issues, location + ".pageId", "non-zero lowercase UUID in D layout");
        try
        {
            SchemaV2PageValueCodec.ValidateName(row.Name, nameof(row.Name));
        }
        catch (ArgumentException)
        {
            Invalid(issues, location + ".name", "canonical Schema V2 page name");
        }

        if (!ArchiveV1ContentModel2Format.IsCanonicalTimestamp(row.CreateTime))
            Invalid(issues, location + ".createTime", "yyyy-MM-dd HH:mm:ss.fffffff");
        if (!ArchiveV1ContentModel2Format.IsCanonicalTimestamp(row.UpdateTime))
            Invalid(issues, location + ".updateTime", "yyyy-MM-dd HH:mm:ss.fffffff");
        if (row.Revision <= 0)
            Invalid(issues, location + ".revision", "positive signed 64-bit integer");
        try
        {
            SchemaV2TagCodec.Decode(row.Tags);
        }
        catch (Exception exception) when (exception is ArgumentNullException or InvalidDataException)
        {
            Invalid(issues, location + ".tags", "canonical Schema V2 tags JSON");
        }
        if (row.Text is null)
            Invalid(issues, location + ".text", "string");
        return issues;
    }

    private static void Invalid(
        ICollection<ArchiveV1ValidationIssue> issues,
        string location,
        string expected)
    {
        issues.Add(new ArchiveV1ValidationIssue(
            ArchiveV1IssueCode.InvalidPropertyValue,
            location,
            expected));
    }
}
