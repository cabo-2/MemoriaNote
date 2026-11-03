using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;

namespace MemoriaNote.Persistence.SchemaV2;

internal enum SchemaV2PageNameValidationError
{
    None,
    EmptyOrWhitespace,
    OuterWhitespace,
    InvalidUnicode,
    ControlCharacter,
    ScalarLimitExceeded
}

internal readonly record struct SchemaV2PageNameValidationFailure(
    SchemaV2PageNameValidationError Error,
    int? Actual = null,
    int? Limit = null);

/// <summary>Validates and converts authoritative Schema V2 page values.</summary>
internal static class SchemaV2PageValueCodec
{
    internal const int MaximumNameScalarCount = 255;
    internal const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    internal static void ValidateName(string name, string parameterName)
    {
        if (!TryValidateName(name, out var failure))
            throw new ArgumentException(MessageFor(failure.Error), parameterName);
    }

    internal static bool TryValidateName(
        string name,
        out SchemaV2PageNameValidationFailure failure)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            failure = new SchemaV2PageNameValidationFailure(
                SchemaV2PageNameValidationError.EmptyOrWhitespace);
            return false;
        }
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            failure = new SchemaV2PageNameValidationFailure(
                SchemaV2PageNameValidationError.OuterWhitespace);
            return false;
        }

        var scalarCount = 0;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                failure = new SchemaV2PageNameValidationFailure(
                    SchemaV2PageNameValidationError.InvalidUnicode);
                return false;
            }
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                failure = new SchemaV2PageNameValidationFailure(
                    SchemaV2PageNameValidationError.ControlCharacter);
                return false;
            }

            scalarCount++;
            remaining = remaining[consumed..];
        }

        if (scalarCount > MaximumNameScalarCount)
        {
            failure = new SchemaV2PageNameValidationFailure(
                SchemaV2PageNameValidationError.ScalarLimitExceeded,
                scalarCount,
                MaximumNameScalarCount);
            return false;
        }

        failure = default;
        return true;
    }

    internal static string FormatTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    internal static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTime.TryParseExact(
                value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidDataException("A persisted page timestamp is not canonical.");
        }

        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
    }

    internal static string ToStoragePageIdPrefix(string normalizedPrefix)
    {
        if (normalizedPrefix == null)
            throw new ArgumentNullException(nameof(normalizedPrefix));
        if (normalizedPrefix.Length is < 4 or > 32)
        {
            throw new ArgumentException(
                "A Page ID prefix must contain 4 to 32 hexadecimal characters.",
                nameof(normalizedPrefix));
        }

        foreach (var character in normalizedPrefix)
        {
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f')))
            {
                throw new ArgumentException(
                    "A Page ID prefix must be lowercase hexadecimal text.",
                    nameof(normalizedPrefix));
            }
        }

        var builder = new StringBuilder(normalizedPrefix.Length + 4);
        for (var index = 0; index < normalizedPrefix.Length; index++)
        {
            if (index is 8 or 12 or 16 or 20)
                builder.Append('-');
            builder.Append(normalizedPrefix[index]);
        }

        return builder.ToString();
    }

    internal static string NextBinaryPrefix(string prefix)
    {
        var characters = prefix.ToCharArray();
        for (var index = characters.Length - 1; index >= 0; index--)
        {
            if (characters[index] == '-')
                continue;
            if (characters[index] == 'f')
                continue;

            characters[index] = characters[index] == '9'
                ? 'a'
                : (char)(characters[index] + 1);
            return new string(characters, 0, index + 1);
        }

        return null;
    }

    private static string MessageFor(SchemaV2PageNameValidationError error) =>
        error switch
        {
            SchemaV2PageNameValidationError.EmptyOrWhitespace =>
                "A page name must not be empty or whitespace.",
            SchemaV2PageNameValidationError.OuterWhitespace =>
                "A page name has leading or trailing whitespace.",
            SchemaV2PageNameValidationError.InvalidUnicode =>
                "A page name is not valid UTF-16.",
            SchemaV2PageNameValidationError.ControlCharacter =>
                "A page name contains a control character.",
            SchemaV2PageNameValidationError.ScalarLimitExceeded =>
                $"A page name must contain at most {MaximumNameScalarCount} Unicode scalars.",
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
}
