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
                using(NoteDbContext db = new NoteDbContext(note.DataSource))
                    foreach(var page in db.PageClient.ReadAll())
                    {
                        var path = Path.Combine(exportDir, $"{page.Name}.txt");
                        using(StreamWriter writer = new StreamWriter(path))
                            writer.Write(page.Text);
                    }
            }, token);
            return task;
        }

        public static Task JsonImporter(this Note note, string importPath)
        {
            var token = new CancellationToken();
            var task = Task.Run(() =>
            {
                using ZipArchive zip = ZipFile.Open(importPath, ZipArchiveMode.Read);
                using NoteDbContext db = new NoteDbContext(note.DataSource);

                using(StreamReader reader = new StreamReader(zip.GetEntry("metadata.json").Open(), Encoding.UTF8))
                {
                    var md = JsonConvert.DeserializeObject<Metadata>(reader.ReadToEnd());                    
                    NoteKeyValue.Set(db, NoteKeyValue.Name, md.Name);
                    NoteKeyValue.Set(db, NoteKeyValue.Title, md.Title);
                    NoteKeyValue.Set(db, NoteKeyValue.Description, md.Description);
                    NoteKeyValue.Set(db, NoteKeyValue.Author, md.Author);  
                    // tags           
                }

                foreach(var entry in zip.Entries.Where(e => e.Name != "metadata.json"))
                {
                    using(StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        var page = JsonConvert.DeserializeObject<Page>(reader.ReadToEnd());
                        db.Pages.Add(page);                                             
                    }
                }

                db.SaveChanges();
            }, token);
            return task;
        }

        public static Task JsonExporter(this Note note, string exportPath)
        {
            var token = new CancellationToken();
            var task = Task.Run(() =>
            {
                using NoteDbContext db = new NoteDbContext(note.DataSource);
                using ZipArchive zip = ZipFile.Open(exportPath, ZipArchiveMode.Create);

                using(StreamWriter writer = new StreamWriter(zip.CreateEntry("metadata.json").Open(), Encoding.UTF8))
                      writer.Write(JsonConvert.SerializeObject(db.Metadata, Formatting.Indented));
   
                foreach(var page in db.PageClient.ReadAll())
                {
                    using(StreamWriter writer = new StreamWriter(zip.CreateEntry(page.Rowid.ToString()+".json").Open(), Encoding.UTF8))
                        writer.Write(JsonConvert.SerializeObject(page, Formatting.Indented));
                }
            }, token);
            return task;
        }
    }

    public static class GuidExtensions
    {
        public static string ToHashId(this Guid guid) => guid.ToString("D").Substring(0, 7);

        public static string ToUuid(this Guid guid) => guid.ToString("D");
    }
}
