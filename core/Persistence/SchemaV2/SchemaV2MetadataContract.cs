using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MemoriaNote.Application;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Defines the authoritative metadata contract for Schema V2 notebooks.</summary>
internal static class SchemaV2MetadataContract
{
    internal const string AuthorKey = "Author";
    internal const string CreateTimeKey = "CreateTime";
    internal const string DescriptionKey = "Description";
    internal const string ReadOnlyKey = "ReadOnly";
    internal const string TagKey = "Tag";
    internal const string TitleKey = "Title";

    internal const string RetiredNameKey = "Name";
    internal const string RetiredVersionKey = "Version";

    internal const string TimestampFormat = "yyyyMMddhhmmss";

    private static readonly IReadOnlyList<string> RequiredKeys = Array.AsReadOnly(
    new[]
    {
        AuthorKey,
        CreateTimeKey,
        DescriptionKey,
        ReadOnlyKey,
        TagKey,
        TitleKey
    });

    internal static IReadOnlyList<string> ApplicationManagedKeys => RequiredKeys;

    internal static bool IsRetiredKey(string key) =>
        string.Equals(key, RetiredNameKey, StringComparison.Ordinal) ||
        string.Equals(key, RetiredVersionKey, StringComparison.Ordinal);

    internal static bool IsApplicationManagedKey(string key) =>
        RequiredKeys.Contains(key, StringComparer.Ordinal);

    internal static string FormatCreateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    internal static bool IsValidCreateTime(string value) =>
        value is not null &&
        DateTime.TryParseExact(
            value,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

    internal static void ValidateUpdate(string key, string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key, parameterName);
        if (IsRetiredKey(key))
        {
            throw new ArgumentException(
                $"Metadata key '{key}' is retired and cannot be stored in Schema V2.",
                parameterName);
        }

        if (!IsApplicationManagedKey(key))
            return;

        if (key == CreateTimeKey)
        {
            if (value is not null && !IsValidCreateTime(value))
            {
                throw new ArgumentException(
                    $"Metadata key '{CreateTimeKey}' must be null or use {TimestampFormat}.",
                    parameterName);
            }
            return;
        }

        if (value is null)
        {
            throw new ArgumentException(
                $"Metadata key '{key}' cannot have a null value.",
                parameterName);
        }

        if (key == ReadOnlyKey &&
            !string.Equals(value, bool.TrueString, StringComparison.Ordinal) &&
            !string.Equals(value, bool.FalseString, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Metadata key '{ReadOnlyKey}' must be 'True' or 'False'.",
                parameterName);
        }
    }

    internal static IReadOnlyList<string> ValidateTransferMetadata(
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var issues = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in metadata)
        {
            if (row is null || row.Key is null)
            {
                issues.Add("A metadata record has a null key.");
                continue;
            }

            if (!values.TryAdd(row.Key, row.Value))
            {
                issues.Add($"Metadata key '{row.Key}' is present more than once.");
                continue;
            }

            try
            {
                ValidateUpdate(row.Key, row.Value, nameof(metadata));
            }
            catch (ArgumentException exception)
            {
                issues.Add(exception.Message);
            }
        }

        foreach (var required in RequiredKeys)
        {
            if (!values.ContainsKey(required))
                issues.Add($"Required metadata key '{required}' is missing.");
        }

        return issues;
    }
}
