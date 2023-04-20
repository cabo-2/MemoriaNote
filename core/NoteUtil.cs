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
    public static class NoteUtil
    {
        static readonly string MetadataName = "metadata.json";

        public static Task TextImporter(Note note, string importDir, bool recursive = false)
        {
            var token = new CancellationToken();
            var task = Task.Run(() =>
            {
                FindText(importDir, recursive, null, (file, subDir) =>
                {
                   using (StreamReader reader = file.OpenText())
                    {
                        var name = TextUtil.ReplaceNameStringReverse(Path.GetFileNameWithoutExtension(file.Name));                        
                        note.CreatePage(name, reader.ReadToEnd(), TextUtil.ConvertGenericPath(subDir));
                    }
                });

            }, token);
            return task;
        }

        static void FindText(string importDir, bool recursive, string subDir, Action<FileInfo, string> importFile)
        {
            var targetDir = subDir == null ? importDir : Path.Combine(importDir, subDir);
            foreach (var file in new DirectoryInfo(targetDir).GetFiles("*.txt"))
                importFile(file, subDir);

            if (!recursive)
                return;

            foreach (var dir in new DirectoryInfo(targetDir).EnumerateDirectories()
                                                            .Where(d => d.Name.FirstOrDefault() != '.'))
                FindText(importDir, recursive, Path.GetRelativePath(importDir, dir.FullName), importFile);
        }

        public static Task TextExporter(Note note, string exportDir)
        {
            var token = new CancellationToken();
            var task = Task.Run(() =>
            {
                using (NoteDbContext db = new NoteDbContext(note.DataSource))
                {
                    foreach (var subDir in db.PageClient.ReadAll()
                                                     .Where(p => p.TagDict.ContainsKey(PageTag.Dir))
                                                     .Select(p => p.TagDict[PageTag.Dir])
                                                     .Distinct())
                    {
                        var dir = Path.Combine(exportDir, TextUtil.ConvertSystemPath(subDir));
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                    }

                    foreach (var page in db.PageClient.ReadAll())
                    {
                        string subDir, path;
                        if (page.TagDict.ContainsKey(PageTag.Dir))
                        {
                            subDir = page.TagDict[PageTag.Dir];
                            path = Path.Combine(exportDir, TextUtil.ConvertSystemPath(subDir), $"{page.Name}.txt");
                        }
                        else
                        {
                            subDir = null;
                            path = Path.Combine(exportDir, $"{page.Name}.txt");                            
                        }
                    
                        using (StreamWriter writer = new StreamWriter(path))
                            writer.Write(page.Text);
                    }
                }
            }, token);
            return task;
        }

        public static Task<Note> Restore(string inputPath, string outputDir)
        {
            if (inputPath == null)
                throw new ArgumentNullException(nameof(inputPath));
            if (outputDir == null)
                throw new ArgumentNullException(nameof(outputDir));

            var token = new CancellationToken();
            var task = Task.Run<Note>(() =>
            {
                using ZipArchive zip = ZipFile.Open(inputPath, ZipArchiveMode.Read);
                var kv = DeserializeNoteKeyValues(zip);
                var name = kv.First(x => x.Key == NoteKeyValue.Name).Value;
                var title = kv.First(x => x.Key == NoteKeyValue.Title).Value;
                var notePath = GetNotePath(outputDir, name);

                Note note = Note.Create(name, title, notePath);
                note.Metadata.Description = kv.FirstOrDefault(x => x.Key == NoteKeyValue.Description)?.Value;
                note.Metadata.Author = kv.FirstOrDefault(x => x.Key == NoteKeyValue.Author)?.Value;

                using (NoteDbContext db = new NoteDbContext(note.DataSource))
                {
                    foreach (var page in DeserializePages(zip))
                        db.Pages.Add(page);
                    db.SaveChanges();
                }

                Note.Migrate(note.DataSource);
                return note;
            }, token);
            return task;
        }

        public static List<NoteKeyValue> DeserializeNoteKeyValues(ZipArchive zip)
        {
            using (StreamReader reader = new StreamReader(zip.GetEntry(MetadataName).Open(), Encoding.UTF8))
                return JsonConvert.DeserializeObject<List<NoteKeyValue>>(reader.ReadToEnd());
        }

        public static IEnumerable<Page> DeserializePages(ZipArchive zip)
        {
            foreach (var entry in zip.Entries.Where(e => e.Name != MetadataName))
                using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    yield return JsonConvert.DeserializeObject<Page>(reader.ReadToEnd());
        }

        public static Task Backup(Note note, string outputPath)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            if (outputPath == null)
                throw new ArgumentNullException(nameof(outputPath));

            var token = new CancellationToken();
            var task = Task.Run(() =>
            {
                using NoteDbContext db = new NoteDbContext(note.DataSource);
                using ZipArchive zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

                int digits = db.Pages.Count().ToString().Length;
                foreach (var page in db.PageClient.ReadAll())
                {
                    using (Stream stream = zip.CreateEntry($"{page.Rowid.ToString().PadLeft(digits, '0')}.json").Open())
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                        writer.Write(JsonConvert.SerializeObject(page, Formatting.Indented));
                }

                using (Stream stream = zip.CreateEntry(MetadataName).Open())
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                    writer.Write(JsonConvert.SerializeObject(db.Metadata, Formatting.Indented));
            }, token);
            return task;
        }

        public static string GetNotePath(string dir, string name)
        {
            string path = Path.Combine(dir, name + ".db");
            if (File.Exists(path))
            {
                path = Path.Combine(dir, name + "_" + DateTime.Now.ToString("yyyyMMddhhmmss") + ".db");
                if (File.Exists(path))
                    throw new ArgumentException(path);
            }
            return path;
        }

        public static string GetJsonPath(string dir, string name)
             => Path.Combine(dir, $"{name}_{DateTime.Now.ToString("yyyyMMddhhmmss")}.json.zip");
    }
}