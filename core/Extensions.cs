﻿using System;
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
    public static class Int32Extensions
    {
        static string[] _numbers = new string[] { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹" };
        public static string ToIndexString(this int self)
        {
            List<string> buffer = new List<string>();
            while (self > 0)
            {
                var rem = self % 10;
                var div = (int)(self / 10);
                buffer.Add(_numbers[rem]);
                self = div;
            }
            buffer.Reverse();
            return string.Join("", buffer);
        }
    }

    public static class StringExtensions
    {
        static char[] _numbers = new char[] { '⁰', '¹', '²', '³', '⁴', '⁵', '⁶', '⁷', '⁸', '⁹' };
        public static string RemoveIndexString(this string self)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var ch in self)
                if (!_numbers.Contains(ch))
                    builder.Append(ch);
            return builder.ToString();
        }

        public static bool CaseInsensitiveContains(this string text, string value,
            StringComparison stringComparison = StringComparison.CurrentCultureIgnoreCase)
        {
            return text.IndexOf(value, stringComparison) >= 0;
        }

        public static string FirstCharToUpper(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.First().ToString().ToUpperInvariant() + input.Substring(1);
        }

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

    public static class EnumExtensions
    {
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

    public static class EnumerableExtensions
    {
        public static int GetOrderIndependentHashCode<T> (this IEnumerable<T> source) {
            int hash = 0;
            foreach (T element in source) {
                hash = hash ^ EqualityComparer<T>.Default.GetHashCode (element);
            }
            return hash;
        }
    }

    public static class GuidExtensions
    {
        public static string ToHashId(this Guid guid) => guid.ToString("D").Substring(0, 7);

        public static string ToUuid(this Guid guid) => guid.ToString("D");
    }

    public static class DateTimeExtensions
    {
        public static string ToDateString(this DateTime dateTime) => dateTime.ToString("yyyyMMddhhmmss");
    }
}
