using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MemoriaNote.Transfer
{
    /// <summary>
    /// Encodes page names as portable file-name components and decodes them again.
    /// </summary>
    public sealed class PageFileNameCodec
    {
        const string EncodingMarker = "~mn~1~";
        const string InvalidFileNameCharacters = "<>:\"/\\|?*";

        static readonly HashSet<string> ReservedDeviceNames = CreateReservedDeviceNames();

        /// <summary>
        /// Encodes a page name as a portable file-name component.
        /// </summary>
        /// <param name="pageName">The original page name.</param>
        /// <returns>A portable file-name component without an extension.</returns>
        public string Encode(string pageName)
        {
            if (pageName == null)
                throw new ArgumentNullException(nameof(pageName));

            if (!RequiresEncoding(pageName))
                return pageName;

            var encoded = new StringBuilder(EncodingMarker);
            var trailingStart = FindTrailingSpaceOrPeriodStart(pageName);
            for (var index = 0; index < pageName.Length; index++)
            {
                var character = pageName[index];
                if (RequiresCharacterEscape(character) || index >= trailingStart)
                {
                    encoded.Append('%');
                    encoded.Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    encoded.Append(character);
                }
            }

            return encoded.ToString();
        }

        /// <summary>
        /// Decodes a portable file-name component into its original page name.
        /// </summary>
        /// <param name="fileName">The file-name component without an extension.</param>
        /// <returns>The decoded page name, or the input when it is not canonically encoded.</returns>
        public string Decode(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            if (!fileName.StartsWith(EncodingMarker, StringComparison.Ordinal))
                return fileName;

            var payload = fileName.Substring(EncodingMarker.Length);
            var decoded = new StringBuilder(payload.Length);
            for (var index = 0; index < payload.Length; index++)
            {
                if (payload[index] != '%')
                {
                    decoded.Append(payload[index]);
                    continue;
                }

                if (index + 2 >= payload.Length ||
                    !TryParseHex(payload[index + 1], payload[index + 2], out var value))
                {
                    return fileName;
                }

                decoded.Append((char)value);
                index += 2;
            }

            var pageName = decoded.ToString();
            return string.Equals(Encode(pageName), fileName, StringComparison.Ordinal)
                ? pageName
                : fileName;
        }

        static bool RequiresEncoding(string pageName)
        {
            if (pageName.StartsWith(EncodingMarker, StringComparison.Ordinal) ||
                pageName == "." ||
                pageName == ".." ||
                IsReservedDeviceName(pageName))
            {
                return true;
            }

            if (FindTrailingSpaceOrPeriodStart(pageName) < pageName.Length)
                return true;

            foreach (var character in pageName)
            {
                if (IsInvalidFileNameCharacter(character))
                    return true;
            }

            return false;
        }

        static bool RequiresCharacterEscape(char character)
        {
            return character == '%' || IsInvalidFileNameCharacter(character);
        }

        static bool IsInvalidFileNameCharacter(char character)
        {
            return character <= 31 ||
                InvalidFileNameCharacters.IndexOf(character) >= 0;
        }

        static int FindTrailingSpaceOrPeriodStart(string value)
        {
            var index = value.Length;
            while (index > 0 && (value[index - 1] == ' ' || value[index - 1] == '.'))
                index--;

            return index;
        }

        static bool IsReservedDeviceName(string pageName)
        {
            var extensionSeparator = pageName.IndexOf('.');
            var baseName = extensionSeparator < 0
                ? pageName
                : pageName.Substring(0, extensionSeparator);
            return ReservedDeviceNames.Contains(baseName.TrimEnd(' ', '.'));
        }

        static HashSet<string> CreateReservedDeviceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                "CLOCK$",
                "COM¹",
                "COM²",
                "COM³",
                "LPT¹",
                "LPT²",
                "LPT³"
            };
            for (var number = 1; number <= 9; number++)
            {
                names.Add("COM" + number.ToString(CultureInfo.InvariantCulture));
                names.Add("LPT" + number.ToString(CultureInfo.InvariantCulture));
            }

            return names;
        }

        static bool TryParseHex(char high, char low, out int value)
        {
            var highValue = ParseHexDigit(high);
            var lowValue = ParseHexDigit(low);
            value = highValue * 16 + lowValue;
            return highValue >= 0 && lowValue >= 0;
        }

        static int ParseHexDigit(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;

            return -1;
        }
    }
}
