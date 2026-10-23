using System.Globalization;
using System.Text;

namespace MemoriaNote.DatasetPreparation.Transform;

internal static class PortablePageFileNameEncoder
{
    const string LegacyEncodingMarker = "~mn~1~";
    const string EncodingMarker = "~mn~2~";
    const string InvalidFileNameCharacters = "<>:\"/\\|?*";
    const int MaximumFileNameBytes = 255;

    static readonly HashSet<string> ReservedDeviceNames = CreateReservedDeviceNames();

    internal static PageNameEncoding EncodeWithLimit(string pageName, string sourceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageName);
        var encoded = Encode(pageName) + ".txt";
        if (Encoding.UTF8.GetByteCount(encoded) <= MaximumFileNameBytes)
            return new PageNameEncoding(pageName, encoded, false);

        var suffix = " — " + sourceHash[..12];
        var shortened = new StringBuilder();
        foreach (var rune in pageName.EnumerateRunes())
        {
            var candidate = shortened.ToString() + rune + suffix;
            if (Encoding.UTF8.GetByteCount(Encode(candidate) + ".txt") > MaximumFileNameBytes)
                break;
            shortened.Append(rune);
        }
        if (shortened.Length == 0)
            throw new InvalidDataException("A portable page name could not be generated.");
        var resultName = shortened + suffix;
        return new PageNameEncoding(resultName, Encode(resultName) + ".txt", true);
    }

    internal static string Encode(string pageName)
    {
        if (!RequiresEncoding(pageName))
            return pageName;

        var encoded = new StringBuilder();
        var trailingStart = FindTrailingSpaceOrPeriodStart(pageName);
        for (var index = 0; index < pageName.Length; index++)
        {
            var character = pageName[index];
            if (RequiresCharacterEscape(character) || character == '~' || index >= trailingStart)
            {
                encoded.Append('%');
                encoded.Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append(character);
            }
        }
        encoded.Append(EncodingMarker);
        return encoded.ToString();
    }

    static bool RequiresEncoding(string pageName)
    {
        if (pageName.StartsWith(LegacyEncodingMarker, StringComparison.Ordinal) ||
            pageName.EndsWith(EncodingMarker, StringComparison.Ordinal) ||
            pageName is "." or ".." || IsReservedDeviceName(pageName) ||
            FindTrailingSpaceOrPeriodStart(pageName) < pageName.Length)
        {
            return true;
        }
        return pageName.Any(RequiresCharacterEscape);
    }

    static bool RequiresCharacterEscape(char character)
    {
        return character == '%' || character <= 31 ||
            InvalidFileNameCharacters.IndexOf(character) >= 0;
    }

    static int FindTrailingSpaceOrPeriodStart(string value)
    {
        var index = value.Length;
        while (index > 0 && value[index - 1] is ' ' or '.')
            index--;
        return index;
    }

    static bool IsReservedDeviceName(string pageName)
    {
        var separator = pageName.IndexOf('.');
        var stem = separator < 0 ? pageName : pageName[..separator];
        return ReservedDeviceNames.Contains(stem.TrimEnd(' ', '.'));
    }

    static HashSet<string> CreateReservedDeviceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
        };
        for (var number = 1; number <= 9; number++)
        {
            names.Add("COM" + number.ToString(CultureInfo.InvariantCulture));
            names.Add("LPT" + number.ToString(CultureInfo.InvariantCulture));
        }
        return names;
    }
}

internal sealed record PageNameEncoding(string PageName, string FileName, bool WasShortened);
