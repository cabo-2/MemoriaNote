using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MemoriaNote.Archive
{
    internal static class ArchiveV1RecordValidator
    {
        internal static List<ArchiveV1ValidationIssue> ValidateMetadata(
            IReadOnlyList<ArchiveV1MetadataRow> rows,
            string sourceNotebookFormatVersion)
        {
            var issues = new List<ArchiveV1ValidationIssue>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var location = $"metadata.json[{index}]";
                if (row == null)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyType,
                        location,
                        "object"));
                    continue;
                }

                if (row.Key == null)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyType,
                        location + ".key",
                        "string"));
                    continue;
                }

                if (ArchiveV1Format.UnicodeScalarCount(row.Key) > 1024 ||
                    Encoding.UTF8.GetByteCount(row.Key) > 4096)
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyValue,
                        location + ".key",
                        "at most 1024 Unicode scalars and 4096 UTF-8 bytes"));
                }

                if (!keys.Add(row.Key))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.DuplicateMetadataKey,
                        location + ".key"));
                }

                if (row.Key == "ReadOnly" && row.Value != null &&
                    !string.Equals(row.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(row.Value, bool.FalseString, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyValue,
                        location + ".value",
                        "True or False using ASCII case"));
                }

                if (row.Key == "CreateTime" && row.Value != null &&
                    !ArchiveV1Format.IsValidMetadataTime(row.Value))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.InvalidPropertyValue,
                        location + ".value",
                        "yyyyMMddhhmmss"));
                }
            }

            foreach (var required in new[] { "Name", "Title", "Version" })
            {
                if (!keys.Contains(required))
                {
                    issues.Add(new ArchiveV1ValidationIssue(
                        ArchiveV1IssueCode.MissingMetadataKey,
                        "metadata.json",
                        required));
                }
            }

            var version = rows.FirstOrDefault(row => row != null && row.Key == "Version");
            if (version != null &&
                (version.Value == null || version.Value != sourceNotebookFormatVersion))
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.MetadataVersionMismatch,
                    "metadata.json[Version].value",
                    sourceNotebookFormatVersion,
                    version.Value == null ? "null" : "different value"));
            }

            return issues;
        }

        internal static List<ArchiveV1ValidationIssue> ValidatePage(
            ArchiveV1PageRow row,
            string location)
        {
            var issues = new List<ArchiveV1ValidationIssue>();
            if (row == null)
            {
                issues.Add(new ArchiveV1ValidationIssue(
                    ArchiveV1IssueCode.InvalidPropertyType,
                    location,
                    "object"));
                return issues;
            }

            if (row.Rowid <= 0)
                Invalid(issues, location + ".rowid", "positive signed 32-bit integer");
            if (row.Index <= 0)
                Invalid(issues, location + ".index", "positive signed 32-bit integer");
            if (row.IsErased != 0 && row.IsErased != 1)
                Invalid(issues, location + ".isErased", "0 or 1");

            if (row.Uuid == null ||
                !Guid.TryParseExact(row.Uuid, "D", out var uuid) ||
                uuid == Guid.Empty)
            {
                Invalid(issues, location + ".uuid", "non-zero GUID in D layout");
            }

            if (!ArchiveV1Format.IsValidPageTime(row.CreateTime))
                Invalid(issues, location + ".createTime", "supported stored DateTime text");
            if (!ArchiveV1Format.IsValidPageTime(row.UpdateTime))
                Invalid(issues, location + ".updateTime", "supported stored DateTime text");
            if (!ArchiveV1Format.IsValidTags(row.Tags))
                Invalid(issues, location + ".tags", "empty JSON whitespace or a flat JSON object");

            return issues;
        }

        static void Invalid(
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
}
