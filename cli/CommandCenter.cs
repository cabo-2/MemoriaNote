using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using MemoriaNote.Cli.Models;

namespace MemoriaNote.Cli
{
    public class CommandCenter
    {
        public int Find(string name = null)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = GetFindKey(name);
                vm.SearchRange = ConfigurationCli.Instance.State.SearchRange;
                vm.SearchMethod = ConfigurationCli.Instance.State.SearchMethod;

                var sc = new ScreenController();
                sc.RequestHome();

                sc.Start(vm);

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                return -1;
            }
        }

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

        public int Edit(string name = null)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = name;
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;

                var sc = new ScreenController();
                sc.RequestManage();

                sc.Start(vm);

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                return -1;
            }
        }

        public int New(string name)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = name;
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.EditingTitle = name;
                vm.EditingState = TextManageType.Create;

                var sc = new ScreenController();
                sc.RequestManage();
                sc.RequestEditor();

                sc.Start(vm);

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                return -1;
            }
        }

        public int ConfigEdit()
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                bool retry;
                do
                {
                    retry = false;                
                    var editor = Editors.TerminalEditorFactory.Create();
                    editor.FileName = ConfigurationCli.Instance.ConfigurationFilename;
                    editor.TextData = JsonConvert.SerializeObject(ConfigurationCli.Instance, Formatting.Indented);

                    ConfigurationCli config = null;                    
                    if (editor.Edit())
                    {                        
                        try
                        {
                            config = JsonConvert.DeserializeObject<ConfigurationCli>(editor.TextData);
                            ConfigurationCli.Instance = config;
                            ConfigurationCli.Instance.Save();
                            retry = false;
                            Log.Logger.Information("Configuration updated");
                        }
                        catch
                        {
                            config = null; 
                            Log.Logger.Error("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (ReadLineTryAgain())
                                retry = true;
                            else
                                return -1;
                        }
                    }
                    else
                    {
                        config = null;
                        Log.Logger.Information("Configuration edit canceled");
                        Console.WriteLine("Operation was canceled");
                    }
                }
                while (retry);
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int ConfigShow()
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                StringReader reader = new StringReader(
                    JsonConvert.SerializeObject(ConfigurationCli.Instance, Formatting.Indented));

                string line = reader.ReadLine();
                while (line != null)
                {
                    Console.WriteLine(line);
                    line = reader.ReadLine();
                }
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int ConfigInit()
        {
            return 0;
        }

        public int Get(string name, string format = null)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel();

                int skipCount = 0;
                int takeCount = ConfigurationCli.Instance.Search.MaxViewResultCount;

                var wg = vm.Workgroup;
                var result = wg.SearchContents(name, SearchRangeType.Note, skipCount, takeCount);

                foreach (var content in result.Contents)
                {
                    var page = wg.SelectedNote.ReadPage(content);
                    Console.WriteLine(page.ToString());
                    break;
                }

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int List(string name = null, bool completion = false)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = GetFindKey(name);
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.ActivateHandler().Wait();

                if (completion)
                    WriteLineCompletion(vm.Contents, vm.ContentsCount);
                else
                    WriteLineList(vm.Contents, vm.ContentsCount);

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

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

            int num = 1;
            foreach (var name in contents.Select(c => GetFirstWord(c.Name))
                                         .OrderBy(n => n)
                                         .Distinct())
            {
                Console.WriteLine(name);
                if (num >= contents.Count)
                    break;
                num++;
            }           
        }

        static string GetFirstWord(string name) => name.Split(' ').FirstOrDefault();

        public int WorkSelect(string name)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

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
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkEdit()
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                var note = vm.Workgroup.SelectedNote;
                bool retry;
                do
                {
                    retry = false;     
                    JsonMetadata data = JsonMetadata.Create(note.Metadata);
                    List<string> errors = new List<string>();
                    var editor = Editors.TerminalEditorFactory.Create();
                    editor.FileName = note.ToString();
                    editor.TextData = JsonConvert.SerializeObject(data, Formatting.Indented);
                 
                    if (editor.Edit())
                    {                        
                        try
                        {
                            data = JsonConvert.DeserializeObject<JsonMetadata>(editor.TextData);

                            data.ValidateName(note, vm.Workgroup, ref errors);
                            data.ValidateTitle(note, vm.Workgroup, ref errors);
                            
                            note.Metadata.CopyTo(data);                      
                            retry = false;                            
                            Log.Logger.Information("Metadata updated");
                        }
                        catch(ValidationException)
                        {
                            data = null;
                            foreach(var error in errors)
                            {
                                Log.Logger.Error($"Error: {error}");
                                Console.Error.WriteLine($"Error: {error}");
                            }
                            if (ReadLineTryAgain())
                                retry = true;
                            else
                                return -1;
                        }
                        catch
                        {
                            data = null; 
                            Log.Logger.Error("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (ReadLineTryAgain())
                                retry = true;
                            else
                                return -1;
                        }
                    }
                    else
                    {
                        data = null;
                        Log.Logger.Information("Metadata edit canceled");
                        Console.WriteLine("Operation was canceled");
                    }
                }
                while (retry);
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkCreate(string name = null, string title = null)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();

                var vm = new MemoriaNoteViewModel();
                var wg = vm.Workgroup;

                if (name == null)
                    name = ReadLineNoteName();

                bool retry = false;
                do
                {
                    if (wg.Notes.Any(n => name == n.Metadata.Name))
                    {
                        Console.Error.WriteLine("Error: A note with that name already exists");
                        if (!ReadLineTryAgain())
                            return -1;

                        name = ReadLineNoteName();
                        retry = true;
                    }
                }
                while (retry);

                if (title == null)
                    title = ReadLineNoteTitle();

                if (string.IsNullOrWhiteSpace(title))
                    title = name;

                var dir = ConfigurationCli.Instance.ApplicationDataDirectory;
                string path = NoteUtil.GetNotePath(dir, name);

                Note note = null;
                try
                {
                    note = Note.Create(name, title, path);
                }
                catch (Exception e)
                {
                    Log.Logger.Error(e.Message);
                    Console.WriteLine($"Error: {e.Message}");
                    return -1;
                }

                WorkAdd(path);
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

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

        public int WorkList()
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();

                foreach (var note in vm.Workgroup.Notes)
                {
                    bool check = note == vm.Workgroup.SelectedNote;
                    StringBuilder buffer = new StringBuilder();
                    buffer.Append(check ? "*" : " ");
                    buffer.Append(" ");
                    buffer.Append(note.ToString());
                    Console.WriteLine(buffer.ToString());
                }

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkAdd(string path)
        {
            try
            {
                if (path == null)
                    throw new ArgumentNullException(nameof(path));

                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("Error: No such file");
                    return -1;
                }

                ConfigurationCli.Instance = ConfigurationCli.Create();
                try
                {
                    using (NoteDbContext db = new NoteDbContext(path)) { }
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

                var vm = new MemoriaNoteViewModel();

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkRemove(string name)
        {
            try
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

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

                var dataSource = wg.Notes.First(n => name == n.Metadata.Name).DataSource;
                ConfigurationCli.Instance.Workgroup.UseDataSources.Remove(dataSource);

                ConfigurationCli.Instance.Save();
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkBackup(string name = null, string outputPath = null)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();
                Note current = null;

                if (name != null)
                {
                    current = vm.Workgroup.Notes.FirstOrDefault(n => n.Metadata.Name == name);
                    if (current == null)
                    {
                        Console.Error.WriteLine("Error: No such name");
                        return -1;
                    }
                }
                else
                {
                    current = vm.Workgroup.SelectedNote;
                }

                if (outputPath != null)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
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
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int WorkRestore(string inputPath, string outputDir = null)
        {
            try
            {
                if (inputPath == null)
                    throw new ArgumentNullException(nameof(inputPath));

                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine("Error: No such input file");
                    return -1;
                }

                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();

                if (outputDir != null)
                {
                    if (Directory.Exists(outputDir))
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
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int Import(string importDir)
        {
            try
            {
                if (importDir == null)
                    throw new ArgumentNullException(nameof(importDir));

                if (!Directory.Exists(importDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }

                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();

                NoteUtil.TextImporter(vm.Workgroup.SelectedNote, importDir).Wait();

                Console.WriteLine("Import completed");
                return 0;
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        public int Export(string exportDir)
        {
            try
            {
                if (exportDir == null)
                    throw new ArgumentNullException(nameof(exportDir));

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
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }
    }
}