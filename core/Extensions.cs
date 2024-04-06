using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;

namespace MemoriaNote
{
    /// <summary>
    /// This class contains extension methods for integers
    /// </summary>
    public static class Int32Extensions
    {
        // Array of special characters used for representing numbers in index format
        static string[] _numbers = new string[] { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹" };

        /// <summary>
        /// Converts an integer to a string representation in index format
        /// </summary>
        /// <param name="self"></param>
        /// <returns></returns>
        public static string ToIndexString(this int self)
        {
            List<string> buffer = new List<string>();
            // Iterate through each digit of the integer
            while (self > 0)
            {
                var rem = self % 10; // Get the remainder of the division by 10
                var div = (int)(self / 10); // Get the quotient of the division by 10
                buffer.Add(_numbers[rem]); // Add the corresponding special character for the current digit
                self = div; // Update the integer to the quotient for the next iteration
            }
            buffer.Reverse(); // Reverse the order of the characters in the buffer
            return string.Join("", buffer); // Combine all characters in the buffer to form the index string
        }
    }

    /// <summary>
    /// This class contains extension methods for strings
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Array of special characters used for representing numbers in index format
        /// </summary>
        static char[] _numbers = new char[] { '⁰', '¹', '²', '³', '⁴', '⁵', '⁶', '⁷', '⁸', '⁹' };

        /// <summary>
        /// Removes index characters from a string
        /// </summary>
        /// <param name="self">The input string</param>
        /// <returns>The string without index characters</returns>
        public static string RemoveIndexString(this string self)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var ch in self)
                if (!_numbers.Contains(ch))
                    builder.Append(ch);
            return builder.ToString();
        }

        /// <summary>
        /// Checks if a string contains a specific value in a case-insensitive manner
        /// </summary>
        /// <param name="text">The input string</param>
        /// <param name="value">The value to search for</param>
        /// <param name="stringComparison">The string comparison type</param>
        /// <returns>True if the value is found, otherwise false</returns>
        public static bool CaseInsensitiveContains(this string text, string value,
            StringComparison stringComparison = StringComparison.CurrentCultureIgnoreCase)
        {
            return text.IndexOf(value, stringComparison) >= 0;
        }

        /// <summary>
        /// Converts the first character of a string to uppercase
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>The input string with the first character in uppercase</returns>
        public static string FirstCharToUpper(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.First().ToString().ToUpperInvariant() + input.Substring(1);
        }

        /// <summary>
        /// Calculates a hash value for the input string
        /// </summary>
        /// <param name="self">The input string</param>
        /// <returns>The calculated hash value as a string</returns>
        public static string CalculateHash(this string self)
        {
            UInt64 hashedValue = 3074457345618258791ul;
            for (int i = 0; i < self.Length; i++)
            {
                hashedValue += self[i];
                hashedValue *= 3074457345618258799ul;
            }
            StringBuilder buffer = new StringBuilder();
            foreach (var value in BitConverter.GetBytes(hashedValue))
            {
                buffer.Append(value.ToString("X2"));
            }
            return buffer.ToString();
        }
    }

    /// <summary>
    /// This class contains extension methods for enums
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Converts the SearchRangeType enum value to a display string
        /// </summary>
        /// <param name="range">The SearchRangeType enum value</param>
        /// <returns>The display string corresponding to the enum value</returns>
        public static string ToDisplayString(this SearchRangeType range)
        {
            switch (range)
            {
                case SearchRangeType.Note:
                    return "A note   ";
                case SearchRangeType.Workgroup:
                default:
                    return "All notes";
            }
        }

        /// <summary>
        /// Converts the SearchMethodType enum value to a display string
        /// </summary>
        /// <param name="method">The SearchMethodType enum value</param>
        /// <returns>The display string corresponding to the enum value</returns>
        public static string ToDisplayString(this SearchMethodType method)
        {
            switch (method)
            {
                case SearchMethodType.Heading:
                    return "Heading  ";
                case SearchMethodType.FullText:
                default:
                    return "Full text";
            }
        }
    }

    /// <summary>
    /// This class contains extension methods for enumerable collections
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Calculates a hash code for an enumerable collection in an order-independent manner
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection</typeparam>
        /// <param name="source">The enumerable collection</param>
        /// <returns>The calculated hash code</returns>
        public static int GetOrderIndependentHashCode<T>(this IEnumerable<T> source)
        {
            int hash = 0;
            foreach (T element in source)
            {
                hash = hash ^ EqualityComparer<T>.Default.GetHashCode(element);
            }
            return hash;
        }
    }

    /// <summary>
    /// This class contains extension methods for Guids
    /// </summary>
    public static class GuidExtensions
    {
        /// <summary>
        /// Converts a Guid to a hashed id by taking the first 7 characters of the Guid string representation
        /// </summary>
        /// <param name="guid">The input Guid</param>
        /// <returns>The hashed id string</returns>
        public static string ToHashId(this Guid guid) => guid.ToString("D").Substring(0, 7);

        /// <summary>
        /// Converts a Guid to a UUID representation
        /// </summary>
        /// <param name="guid">The input Guid</param>
        /// <returns>The UUID string</returns>
        public static string ToUuid(this Guid guid) => guid.ToString("D");
    }

    /// <summary>
    /// This class contains extension methods for DateTimes
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Converts a DateTime to a string representation in the format "yyyyMMddhhmmss"
        /// </summary>
        /// <param name="dateTime">The input DateTime</param>
        /// <returns>The string representation of the DateTime in the specified format</returns>
        public static string ToDateString(this DateTime dateTime) => dateTime.ToString("yyyyMMddhhmmss");
    }
}
