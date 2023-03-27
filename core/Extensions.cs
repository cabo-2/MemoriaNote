﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

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
                case SearchMethodType.Headline:
                    return "Heading  ";
                case SearchMethodType.FullText:   
                default:
                    return "Full text";
            }
        }
    }

    public static class IOExtensions
    {
        public static Task TextImporter(this Note note, string importDir)
        {
            var token = new CancellationToken();
            var task = Task.Run(() => {
                foreach(var file in new DirectoryInfo(importDir).GetFiles("*.txt"))
                {
                    using(StreamReader reader = file.OpenText())
                        note.Create(Path.GetFileNameWithoutExtension(file.Name), reader.ReadToEnd());
                }
            }, token);
            return task;
        }

        public static Task TextExporter(this Note note, string exportDir)
        {
            var token = new CancellationToken();
            var task = Task.Run(() => {
                foreach(var page in note.ReadAll())
                {
                    var path = Path.Combine(exportDir, $"{page.Title}.txt");
                    using(StreamWriter writer = new StreamWriter(path))
                        writer.Write(page.Text);
                }
            }, token);
            return task;
        }
    }
}
