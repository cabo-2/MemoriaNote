using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Reactive.Concurrency;
using System.Threading;
using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the command center for managing various actions within the application.
    /// </summary>
    public class CommandCenter
    {
        readonly INoteMigrator _noteMigrator;

        /// <summary>
        /// Initializes a command center with the default SQLite persistence services.
        /// </summary>
        public CommandCenter() : this(NotePersistence.CreateMigrator())
        {
        }

        /// <summary>
        /// Initializes a command center with an explicit note migrator.
        /// </summary>
        /// <param name="noteMigrator">The service used for note database lifecycle operations.</param>
        public CommandCenter(INoteMigrator noteMigrator)
        {
            _noteMigrator = noteMigrator ??
                throw new ArgumentNullException(nameof(noteMigrator));
        }

        /// <summary>
        /// 共通の前後処理＋例外ハンドリングを行うラッパー
        /// </summary>
        private static int Execute(Func<int> body)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                // 例外はすべてログ＆標準エラー出力
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to find a specific entry by name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public int Find(string name = null)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel
                {
                    SearchEntry = GetFindKey(name),
                    SearchRange = ConfigurationCli.Instance.State.SearchRange,
                    SearchMethod = ConfigurationCli.Instance.State.SearchMethod
                };

                var sc = new ScreenController();
                sc.RequestHome();
                sc.Start(vm);

                ConfigurationCli.Instance.Save();
                return 0;
            });

        static string GetFindKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var match = TextMatching.Create(name);
            if (match.IsSentence)
                return name;

            if (match.IsPrefixMatch || match.IsSuffixMatch)
                return name;

            return name + "*";
        }

        /// <summary>
        /// Method to edit a specific entry by name
        /// </summary>
        /// <param name="name">The name of the entry to be edited</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Edit(string name = null)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel
                {
                    SearchEntry = name,
                    SearchRange = SearchRangeType.Note,
                    SearchMethod = SearchMethodType.Heading
                };
                var sc = new ScreenController();
                sc.RequestManage();
                sc.Start(vm);
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to create a new entry with the provided name
        /// </summary>
        /// <param name="name">The name of the new entry</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int New(string name)
            => Execute(() =>
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel
                {
                    SearchEntry = name,
                    SearchRange = SearchRangeType.Note,
                    SearchMethod = SearchMethodType.Heading,
                    EditingTitle = name,
                    EditingState = TextManageType.Create
                };
                var sc = new ScreenController();
                sc.RequestManage();
                sc.RequestEditor();
                sc.Start(vm);
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to edit the configuration settings
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int ConfigEdit()
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                bool retry;
                do
                {
                    retry = false;
                    var editor = Editors.TerminalEditorFactory.Create();
                    editor.FileName = ConfigurationCli.Instance.ConfigurationFilename;
                    editor.TextData = JsonConvert.SerializeObject(ConfigurationCli.Instance, Formatting.Indented);

                    if (editor.Edit())
                    {
                        try
                        {
                            var config = JsonConvert.DeserializeObject<ConfigurationCli>(editor.TextData);
                            ConfigurationCli.Instance = config;
                            ConfigurationCli.Instance.Save();
                            Log.Logger.Information("Configuration updated");
                        }
                        catch
                        {
                            Log.Logger.Error("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (ReadLineTryAgain()) retry = true;
                            else return -1;
                        }
                    }
                    else
                    {
                        Log.Logger.Information("Configuration edit canceled");
                        Console.WriteLine("Operation was canceled");
                    }
                } while (retry);
                return 0;
            });

        /// <summary>
        /// Method to display the current configuration settings in a formatted way
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int ConfigShow()
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var json = JsonConvert.SerializeObject(ConfigurationCli.Instance, Formatting.Indented);
                using var reader = new StringReader(json);
                string line;
                while ((line = reader.ReadLine()) != null)
                    Console.WriteLine(line);
                return 0;
            });

        public int ConfigInit() => Execute(() => 0);

        /// <summary>
        /// Method to list contents based on the provided name and completion flag
        /// </summary>
        /// <param name="name">The name of the content to search for (default is null)</param>
        /// <param name="completion">Flag to indicate if completion is needed (default is false)</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int List(string name = null, bool completion = false)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                if (completion && ConfigurationCli.Instance.Terminal.Completion == CompletionType.None)
                    return 0;

                var vm = new MemoriaNoteViewModel
                {
                    SearchEntry = GetFindKey(name),
                    SearchRange = SearchRangeType.Note,
                    SearchMethod = SearchMethodType.Heading
                };
                vm.ActivateHandler().Wait();

                if (completion)
                    WriteLineCompletion(vm.Contents, vm.ContentsCount);
                else
                    WriteLineList(vm.Contents, vm.ContentsCount);

                ConfigurationCli.Instance.Save();
                return 0;
            });

        static int GetIndexWidth(int viewCount)
        {
            int indexWidth = 0;
            while (viewCount > 0)
            {
                viewCount /= 10;
                indexWidth++;
            }
            return indexWidth;
        }

        static int GetMaxNameLength(List<Content> list)
        {
            int textWidth = 0;
            foreach (var content in list)
            {
                if (content.Name.Length > textWidth)
                    textWidth = content.Name.Length;
            }
            return Math.Min(textWidth, 64);
        }

        static void AppendBoarder(StringBuilder buffer, int count)
        {
            foreach (var num in Enumerable.Range(0, count))
                buffer.Append("-");
        }

        static void WriteLineBoarder(int indexWidth, int textWidth)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.Append("+");
            AppendBoarder(buffer, indexWidth);
            buffer.Append("-+-");
            AppendBoarder(buffer, 7);
            buffer.Append("-+-");
            AppendBoarder(buffer, textWidth);
            buffer.Append("-+");
            Console.WriteLine(buffer.ToString());
        }

        static void WriteLineList(List<Content> contents, int totalCount)
        {
            if (totalCount < 0)
                throw new ArgumentException(nameof(totalCount));

            int num = 1;
            int indexWidth = GetIndexWidth(contents.Count);
            int nameWidth = GetMaxNameLength(contents);

            WriteLineBoarder(indexWidth, nameWidth);
            foreach (var content in contents)
            {
                var buffer = new StringBuilder();
                buffer.Append("|");
                buffer.Append(num.ToString().PadLeft(indexWidth, '0'));
                buffer.Append(" | ");
                buffer.Append(content.Guid.ToHashId());
                buffer.Append(" | ");
                var name = content.Name;
                buffer.Append(name.Substring(0, Math.Min(name.Length, nameWidth)));
                foreach (var space in Enumerable.Repeat(" ", nameWidth - Math.Min(name.Length, nameWidth)))
                    buffer.Append(space);
                buffer.Append(" |");
                Console.WriteLine(buffer.ToString());

                if (num >= contents.Count)
                    break;
                num++;
            }
            WriteLineBoarder(indexWidth, nameWidth);
            if (contents.Count < totalCount)
                Console.WriteLine("Number of text messages exceeds 1000");

            Console.WriteLine("Total count: " + totalCount.ToString());
        }

        static void WriteLineCompletion(List<Content> contents, int totalCount)
        {
            if (totalCount < 0)
                throw new ArgumentException(nameof(totalCount));

            foreach (var name in contents.Select(c => GetFirstWord(c.Name).ToLower())
                                         .OrderBy(n => n)
                                         .Distinct())
                Console.WriteLine(name);
        }

        static string GetFirstWord(string name) => name.Split(' ').FirstOrDefault();

        /// <summary>
        /// Method to select a specific note by its name
        /// </summary>
        /// <param name="name">The name of the note to select</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkSelect(string name)
            => Execute(() =>
            {
                if (name == null) throw new ArgumentNullException(nameof(name));
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                var wg = vm.Workgroup;
                if (!wg.Notes.Any(n => name == n.Metadata.Name))
                {
                    Console.Error.WriteLine("Error: No such note");
                    return -1;
                }
                ConfigurationCli.Instance.Workgroup.SelectedNoteName = name;
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to edit the metadata of a selected note
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkEdit()
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                var note = vm.Workgroup.SelectedNote;
                bool retry;
                do
                {
                    retry = false;
                    var data = DataSourceTracker.Create(note.Metadata);
                    var errors = new List<string>();
                    var editor = Editors.TerminalEditorFactory.Create();
                    editor.FileName = note.ToString();
                    editor.TextData = JsonConvert.SerializeObject(data, Formatting.Indented);

                    if (editor.Edit())
                    {
                        try
                        {
                            data = JsonConvert.DeserializeObject<DataSourceTracker>(editor.TextData);
                            data.ValidateName(note, vm.Workgroup, ref errors);
                            data.ValidateTitle(note, vm.Workgroup, ref errors);
                            note.UpdateMetadata(
                                NoteMetadataUpdate.FromDifferences(note.Metadata, data));
                            Log.Logger.Information("Metadata updated");
                        }
                        catch (ValidationException)
                        {
                            foreach (var err in errors)
                            {
                                Log.Logger.Error($"Error: {err}");
                                Console.Error.WriteLine($"Error: {err}");
                            }
                            if (ReadLineTryAgain()) retry = true;
                            else return -1;
                        }
                        catch
                        {
                            Log.Logger.Error("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (ReadLineTryAgain()) retry = true;
                            else return -1;
                        }
                    }
                    else
                    {
                        Log.Logger.Information("Metadata edit canceled");
                        Console.WriteLine("Operation was canceled");
                    }
                } while (retry);
                return 0;
            });

        /// <summary>
        /// Method to create a new note with a specified name and title
        /// </summary>
        /// <param name="name">The name of the note to create</param>
        /// <param name="title">The title of the note to create</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkCreate(string name = null, string title = null)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                var wg = vm.Workgroup;
                if (name == null) name = ReadLineNoteName();
                bool retry;
                do
                {
                    retry = false;
                    if (wg.Notes.Any(n => name == n.Metadata.Name))
                    {
                        Console.Error.WriteLine("Error: A note with that name already exists");
                        if (!ReadLineTryAgain()) return -1;
                        name = ReadLineNoteName();
                        retry = true;
                    }
                } while (retry);
                if (title == null) title = ReadLineNoteTitle();
                if (string.IsNullOrWhiteSpace(title)) title = name;
                var path = NoteUtil.GetNotePath(ConfigurationCli.Instance.ApplicationDataDirectory, name);
                try
                {
                    _noteMigrator.CreateAsync(
                            name,
                            title,
                            path,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception e)
                {
                    Log.Logger.Error(e.Message);
                    Console.WriteLine($"Error: {e.Message}");
                    return -1;
                }
                WorkAdd(path);
                return 0;
            });

        static bool ReadLineTryAgain()
        {
            Console.Write("Try again?(y/n)_");
            var key = Console.ReadLine();
            return key.ToLower() == "y";
        }

        static string ReadLineNoteName()
        {
            Console.Write("What is the name?_");
            return Console.ReadLine();
        }

        static string ReadLineNoteTitle()
        {
            Console.Write("What is the title?_");
            return Console.ReadLine();
        }

        /// <summary>
        /// Method to list all notes in the workgroup with an optional flag to show only completed notes
        /// </summary>
        /// <param name="completion">Flag to indicate if only completed notes should be listed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkList(bool completion = false)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                foreach (var note in vm.Workgroup.Notes)
                {
                    if (!completion)
                    {
                        var mark = note == vm.Workgroup.SelectedNote ? "*" : " ";
                        Console.WriteLine($"{mark} {note}");
                    }
                    else
                    {
                        Console.WriteLine(note.Metadata.Name);
                    }
                }
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to add a new note to the workgroup using the specified path
        /// </summary>
        /// <param name="path">The path of the note to be added</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkAdd(string path)
            => Execute(() =>
            {
                if (path == null) throw new ArgumentNullException(nameof(path));
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("Error: No such file");
                    return -1;
                }
                ConfigurationCli.Instance = ConfigurationCli.Create();
                try
                {
                    using var db = new NoteDbContext(path) { };
                }
                catch
                {
                    Console.Error.WriteLine("Error: Failed to load");
                    return -1;
                }
                if (!ConfigurationCli.Instance.DataSources.Contains(path))
                    ConfigurationCli.Instance.DataSources.Add(path);
                if (!ConfigurationCli.Instance.Workgroup.UseDataSources.Contains(path))
                    ConfigurationCli.Instance.Workgroup.UseDataSources.Add(path);
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to remove a note from the workgroup based on the specified name
        /// </summary>
        /// <param name="name">The name of the note to be removed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkRemove(string name)
            => Execute(() =>
            {
                if (name == null) throw new ArgumentNullException(nameof(name));
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                var wg = vm.Workgroup;
                if (!wg.Notes.Any(n => name == n.Metadata.Name))
                {
                    Console.Error.WriteLine("Error: No such remove note");
                    return -1;
                }
                if (wg.Notes.Count == 1)
                {
                    Console.Error.WriteLine("Error: Cannot remove the last note");
                    return -1;
                }
                var ds = wg.Notes.First(n => name == n.Metadata.Name).DataSource;
                ConfigurationCli.Instance.Workgroup.UseDataSources.Remove(ds);
                ConfigurationCli.Instance.Save();
                return 0;
            });

        /// <summary>
        /// Method to backup a note in the workgroup
        /// </summary>
        /// <param name="name">Optional parameter specifying the name of the note to backup</param>
        /// <param name="outputPath">Optional parameter specifying the output path for the backup</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkBackup(string name = null, string outputPath = null)
            => Execute(() =>
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                Note current = name switch
                {
                    null => vm.Workgroup.SelectedNote,
                    _ => vm.Workgroup.Notes.FirstOrDefault(n => n.Metadata.Name == name)
                };
                if (current == null)
                {
                    Console.Error.WriteLine("Error: No such name");
                    return -1;
                }
                if (outputPath != null)
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(dir))
                    {
                        Console.Error.WriteLine("Error: No such output directory");
                        return -1;
                    }
                    if (File.Exists(outputPath))
                    {
                        Console.Error.WriteLine("Error: Output file exists");
                        return -1;
                    }
                }
                else
                {
                    outputPath = NoteUtil.GetJsonPath(Environment.CurrentDirectory, current.Metadata.Name);
                }
                NoteUtil.Backup(current, outputPath).Wait();
                Console.WriteLine("Backup completed");
                return 0;
            });

        /// <summary>
        /// Method to restore a note in the workgroup from a backup file
        /// </summary>
        /// <param name="inputPath">The path to the backup file to be restored</param>
        /// <param name="outputDir">Optional parameter specifying the output directory for the restored note</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkRestore(string inputPath, string outputDir = null)
            => Execute(() =>
            {
                if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine("Error: No such input file");
                    return -1;
                }
                ConfigurationCli.Instance = ConfigurationCli.Create();
                if (outputDir != null)
                {
                    if (!Directory.Exists(outputDir))
                    {
                        Console.Error.WriteLine("Error: No such directory");
                        return -1;
                    }
                }
                else
                {
                    outputDir = Environment.CurrentDirectory;
                }
                NoteUtil.Restore(inputPath, outputDir).Wait();
                Console.WriteLine("Restore completed");
                return 0;
            });

        /// <summary>
        /// Method to import text files into the workgroup
        /// </summary>
        /// <param name="importDir">The directory containing the text files to import</param>
        /// <param name="recursive">Optional parameter specifying whether to import recursively</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Import(string importDir, bool recursive = false)
            => Execute(() =>
            {
                if (importDir == null) throw new ArgumentNullException(nameof(importDir));
                if (!Directory.Exists(importDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                NoteUtil.TextImporter(vm.Workgroup.SelectedNote, importDir, recursive).Wait();
                Console.WriteLine("Import completed");
                return 0;
            });

        /// <summary>
        /// Method to export a note in the workgroup to a text file
        /// </summary>
        /// <param name="exportDir">The directory where the text file will be exported</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Export(string exportDir)
            => Execute(() =>
            {
                if (exportDir == null) throw new ArgumentNullException(nameof(exportDir));
                if (!Directory.Exists(exportDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                NoteUtil.TextExporter(vm.Workgroup.SelectedNote, exportDir).Wait();
                Console.WriteLine("Export completed");
                return 0;
            });
    }
}
