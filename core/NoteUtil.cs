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
    /// Contains utility methods for working with Note objects.
    /// </summary>
    public static class NoteUtil
    {
        static readonly string MetadataName = "metadata.json";

        /// <summary>
        /// Asynchronously imports text files from a specified directory into a Note object.
        /// </summary>
        /// <param name="note">The Note object to import the text into.</param>
        /// <param name="importDir">The directory path from which to import the text files.</param>
        /// <param name="recursive">A flag indicating whether to import text files from subdirectories recursively.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static Task TextImporter(Note note, string importDir, bool recursive = false)
        {
            // Create a cancellation token for the task.
            var token = new CancellationToken();
            // Run the task asynchronously.
            var task = Task.Run(() =>
            {
                // Find text files in the specified directory and its subdirectories.
                FindText(importDir, recursive, null, (file, subDir) =>
                {
                    // Read the contents of the text file.
                    using (StreamReader reader = file.OpenText())
                    {
                        // Extract the name for the page from the file name.
                        var name = TextUtil.ReplaceNameStringReverse(Path.GetFileNameWithoutExtension(file.Name));
                        // Create a new page with the extracted name and the text content.
                        note.CreatePage(name, reader.ReadToEnd(), TextUtil.ConvertGenericPath(subDir));
                    }
                });

            }, token);
            // Return the task as a result of the asynchronous operation.
            return task;
        }

        /// <summary>
        /// Recursively finds text files in the specified directory and its subdirectories, and performs the specified action on each text file found.
        /// </summary>
        /// <param name="importDir">The directory path to search for text files.</param>
        /// <param name="recursive">A flag indicating whether to search for text files in subdirectories recursively.</param>
        /// <param name="subDir">The relative subdirectory path within the import directory.</param>
        /// <param name="importFile">The action to perform on each text file found.</param>
        static void FindText(string importDir, bool recursive, string subDir, Action<FileInfo, string> importFile)
        {
            // Determine the target directory based on the presence of a subdirectory.
            var targetDir = subDir == null ? importDir : Path.Combine(importDir, subDir);

            // Iterate over each text file in the target directory and perform the specified action.
            foreach (var file in new DirectoryInfo(targetDir).GetFiles("*.txt"))
                importFile(file, subDir);

            // If not performing a recursive search, exit the method.
            if (!recursive)
                return;

            // Recursively search for text files in subdirectories.
            foreach (var dir in new DirectoryInfo(targetDir).EnumerateDirectories()
                                                            .Where(d => d.Name.FirstOrDefault() != '.'))
                FindText(importDir, recursive, Path.GetRelativePath(importDir, dir.FullName), importFile);
        }

        /// <summary>
        /// Asynchronously exports text content from a Note object into text files in a specified directory.
        /// </summary>
        /// <param name="note">The Note object containing the text content to export.</param>
        /// <param name="exportDir">The directory path where the text files will be exported.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static Task TextExporter(Note note, string exportDir)
        {
            // Create a cancellation token for the task.
            var token = new CancellationToken();
            // Run the task asynchronously.
            var task = Task.Run(() =>
            {
                // Open a connection to the Note database.
                using (NoteDbContext db = new NoteDbContext(note.DataSource))
                {
                    // Create directories based on unique subdirectories found in the Note pages.
                    foreach (var subDir in db.PageClient.ReadAll()
                                                     .Where(p => p.TagDict.ContainsKey(PageTag.Dir))
                                                     .Select(p => p.TagDict[PageTag.Dir])
                                                     .Distinct())
                    {
                        var dir = Path.Combine(exportDir, TextUtil.ConvertSystemPath(subDir));
                        // Create the directory if it does not exist.
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                    }

                    // Export each page's text content into a text file.
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

                        // Write the page's text content to the text file.
                        using (StreamWriter writer = new StreamWriter(path))
                            writer.Write(page.Text);
                    }
                }
            }, token);
            // Return the task as a result of the asynchronous operation.
            return task;
        }

        /// <summary>
        /// Restores a Note object from a specified input zip file to the specified output directory.
        /// </summary>
        /// <param name="inputPath">The path to the input zip file containing the Note object data.</param>
        /// <param name="outputDir">The directory where the restored Note object will be saved.</param>
        /// <returns>A Task representing the asynchronous operation that restores the Note object.</returns>
        public static Task<Note> Restore(string inputPath, string outputDir)
        {
            // Check for null input parameters and throw ArgumentNullException if necessary.
            if (inputPath == null)
                throw new ArgumentNullException(nameof(inputPath));
            if (outputDir == null)
                throw new ArgumentNullException(nameof(outputDir));

            // Create a cancellation token for the task.
            var token = new CancellationToken();
            // Run the task asynchronously, restoring the Note object from the input zip file.
            var task = Task.Run<Note>(() =>
            {
                // Open the input zip file for reading.
                using ZipArchive zip = ZipFile.Open(inputPath, ZipArchiveMode.Read);
                // Deserialize the Note key values from the zip file.
                var kv = DeserializeNoteKeyValues(zip);
                // Retrieve the name and title of the Note from the key values.
                var name = kv.First(x => x.Key == NoteKeyValue.Name).Value;
                var title = kv.First(x => x.Key == NoteKeyValue.Title).Value;
                // Generate the path for the restored Note.
                var notePath = GetNotePath(outputDir, name);

                // Create a new Note object with the retrieved name, title, and path.
                Note note = Note.Create(name, title, notePath);
                // Set additional metadata for the Note if available.
                note.Metadata.Description = kv.FirstOrDefault(x => x.Key == NoteKeyValue.Description)?.Value;
                note.Metadata.Author = kv.FirstOrDefault(x => x.Key == NoteKeyValue.Author)?.Value;

                // Open a connection to the Note database.
                using (NoteDbContext db = new NoteDbContext(note.DataSource))
                {
                    // Deserialize and add each Page object from the zip file to the database.
                    foreach (var page in DeserializePages(zip))
                        db.Pages.Add(page);
                    // Save changes to the database.
                    db.SaveChanges();
                }

                // Migrate the Note's data source to the latest version.
                Note.Migrate(note.DataSource);
                // Return the restored Note object.
                return note;
            }, token);
            // Return the task as a result of the asynchronous operation.
            return task;
        }

        /// <summary>
        /// Deserialize the Note key values from a specified ZipArchive object.
        /// </summary>
        /// <param name="zip">The ZipArchive object containing the Note key values.</param>
        /// <returns>A list of NoteKeyValue objects deserialized from the ZipArchive.</returns>
        public static List<NoteKeyValue> DeserializeNoteKeyValues(ZipArchive zip)
        {
            using (StreamReader reader = new StreamReader(zip.GetEntry(MetadataName).Open(), Encoding.UTF8))
                return JsonConvert.DeserializeObject<List<NoteKeyValue>>(reader.ReadToEnd());
        }

        /// <summary>
        /// Deserialize the Page objects from a specified ZipArchive object, excluding metadata entries.
        /// </summary>
        /// <param name="zip">The ZipArchive object containing the Page objects.</param>
        /// <returns>An IEnumerable collection of Page objects deserialized from the ZipArchive.</returns>
        public static IEnumerable<Page> DeserializePages(ZipArchive zip)
        {
            foreach (var entry in zip.Entries.Where(e => e.Name != MetadataName))
                using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    yield return JsonConvert.DeserializeObject<Page>(reader.ReadToEnd());
        }

        /// <summary>
        /// Asynchronously creates a backup of the Note object to a specified output path.
        /// </summary>
        /// <param name="note">The Note object to backup.</param>
        /// <param name="outputPath">The path to save the backup file.</param>
        /// <returns>A Task representing the asynchronous operation of creating the backup.</returns>
        public static Task Backup(Note note, string outputPath)
        {
            // Check for null input parameters and throw ArgumentNullException if necessary.
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            if (outputPath == null)
                throw new ArgumentNullException(nameof(outputPath));

            // Create a cancellation token for the task.
            var token = new CancellationToken();
            // Run the task asynchronously to create a backup of the Note object.
            var task = Task.Run(() =>
            {
                // Open a connection to the Note database.
                using NoteDbContext db = new NoteDbContext(note.DataSource);
                // Open the output zip file for writing.
                using ZipArchive zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

                // Determine the number of digits needed to pad page numbers for sorting.
                int digits = db.Pages.Count().ToString().Length;
                // Iterate through each page in the database and write them to the zip file as JSON.
                foreach (var page in db.PageClient.ReadAll())
                {
                    // Create an entry in the zip file for each page and write the JSON content.
                    using (Stream stream = zip.CreateEntry($"{page.Rowid.ToString().PadLeft(digits, '0')}.json").Open())
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                        writer.Write(JsonConvert.SerializeObject(page, Formatting.Indented));
                }

                // Write the metadata of the Note to the zip file as JSON.
                using (Stream stream = zip.CreateEntry(MetadataName).Open())
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                    writer.Write(JsonConvert.SerializeObject(db.Metadata, Formatting.Indented));
            }, token);
            // Return the task as a result of the asynchronous operation.
            return task;
        }

        /// <summary>
        /// Gets the path for a Note object within a specified directory.
        /// If the file already exists, appends the current timestamp to the filename.
        /// </summary>
        /// <param name="dir">The directory path where the Note file should be located.</param>
        /// <param name="name">The name of the Note object.</param>
        /// <returns>The final path for the Note file.</returns>
        public static string GetNotePath(string dir, string name)
        {
            string path = Path.Combine(dir, name + ".db");

            // Check if a file with the same name already exists.
            // If so, append the current timestamp to the filename.
            if (File.Exists(path))
            {
                path = Path.Combine(dir, name + "_" + DateTime.Now.ToString("yyyyMMddhhmmss") + ".db");

                // If the updated filename still exists, throw an exception.
                if (File.Exists(path))
                    throw new ArgumentException(path);
            }
            return path;
        }

        /// <summary>
        /// Gets the path for a JSON file within a specified directory with the current timestamp appended to the filename.
        /// </summary>
        /// <param name="dir">The directory path where the JSON file should be located.</param>
        /// <param name="name">The name of the JSON file.</param>
        /// <returns>The final path for the JSON file with the current timestamp appended.</returns>
        public static string GetJsonPath(string dir, string name)
             => Path.Combine(dir, $"{name}_{DateTime.Now.ToString("yyyyMMddhhmmss")}.json.zip");
    }
}