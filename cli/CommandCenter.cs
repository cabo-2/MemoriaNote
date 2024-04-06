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

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the command center for managing various actions within the application.
    /// </summary>
    public class CommandCenter
    {
        /// <summary>
        /// Method to find a specific entry by name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public int Find(string name = null)
        {
            try
            {
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Initialize a new instance of MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                // Set the search entry based on the provided name
                vm.SearchEntry = GetFindKey(name);
                // Set the search range and method based on the current configuration state
                vm.SearchRange = ConfigurationCli.Instance.State.SearchRange;
                vm.SearchMethod = ConfigurationCli.Instance.State.SearchMethod;

                // Initialize a new instance of ScreenController and request the home screen
                var sc = new ScreenController();
                sc.RequestHome();

                // Start the screen controller with the initialized view model
                sc.Start(vm);

                // Save the updated configuration state
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
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

        /// <summary>
        /// Method to edit a specific entry by name
        /// </summary>
        /// <param name="name">The name of the entry to be edited</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Edit(string name = null)
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Initialize a new instance of MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                // Set the search entry based on the provided name
                vm.SearchEntry = name;
                // Set the search range and method based on the current configuration state
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;

                // Initialize a new instance of ScreenController and request the manage screen
                var sc = new ScreenController();
                sc.RequestManage();

                // Start the screen controller with the initialized view model
                sc.Start(vm);

                // Save the updated configuration state
                ConfigurationCli.Instance.Save();

                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                return -1;
            }
        }

        /// <summary>
        /// Method to create a new entry with the provided name
        /// </summary>
        /// <param name="name">The name of the new entry</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int New(string name)
        {
            try
            {
                // Check if the name is null and throw an exception if it is
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Initialize a new instance of MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = name;
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.EditingTitle = name;
                vm.EditingState = TextManageType.Create;

                // Initialize a new instance of ScreenController and request the manage screen and editor screen
                var sc = new ScreenController();
                sc.RequestManage();
                sc.RequestEditor();

                // Start the screen controller with the initialized view model
                sc.Start(vm);

                // Save the updated configuration state
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                return -1;
            }
        }

        /// <summary>
        /// Method to edit the configuration settings
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
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

        /// <summary>
        /// Method to display the current configuration settings in a formatted way
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int ConfigShow()
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Create a StringReader to read the serialized configuration settings
                StringReader reader = new StringReader(
                    JsonConvert.SerializeObject(ConfigurationCli.Instance, Formatting.Indented));

                // Read each line from the StringReader and output it to the console
                string line = reader.ReadLine();
                while (line != null)
                {
                    Console.WriteLine(line);
                    line = reader.ReadLine();
                }

                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
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

        /// <summary>
        /// Method to list contents based on the provided name and completion flag
        /// </summary>
        /// <param name="name">The name of the content to search for (default is null)</param>
        /// <param name="completion">Flag to indicate if completion is needed (default is false)</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int List(string name = null, bool completion = false)
        {
            try
            {
                // Initialize a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Check if completion is requested and the completion type is not None
                if (completion && ConfigurationCli.Instance.Terminal.Completion == CompletionType.None)
                    return 0;

                // Initialize a new MemoriaNoteViewModel instance
                var vm = new MemoriaNoteViewModel();
                vm.SearchEntry = GetFindKey(name); // Set the search entry based on the provided name
                vm.SearchRange = SearchRangeType.Note;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.ActivateHandler().Wait(); // Activate the handler to get the contents

                // Check if completion is requested and output completion list, otherwise output the content list
                if (completion)
                    WriteLineCompletion(vm.Contents, vm.ContentsCount);
                else
                    WriteLineList(vm.Contents, vm.ContentsCount);

                // Save the updated configuration state
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
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
        {
            try
            {
                // Check if the name parameter is null and throw an exception if so
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();

                // Get the workgroup from the view model
                var wg = vm.Workgroup;

                // Check if there is a note with the provided name in the workgroup
                if (!wg.Notes.Any(n => name == n.Metadata.Name))
                {
                    Console.Error.WriteLine("Error: No such note");
                    return -1;
                }

                // Set the selected note name in the configuration and save the changes
                ConfigurationCli.Instance.Workgroup.SelectedNoteName = name;
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to edit the metadata of a selected note
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkEdit()
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();
                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                // Get the currently selected note
                var note = vm.Workgroup.SelectedNote;
                bool retry;
                do
                {
                    retry = false;
                    // Create a new DataSourceTracker instance using the note's data source
                    DataSourceTracker data = DataSourceTracker.Create(note.Metadata.DataSource);
                    List<string> errors = new List<string>();
                    // Create a new TerminalEditor instance
                    var editor = Editors.TerminalEditorFactory.Create();
                    editor.FileName = note.ToString();
                    editor.TextData = JsonConvert.SerializeObject(data, Formatting.Indented);

                    // If the user edits the data using the editor
                    if (editor.Edit())
                    {
                        try
                        {
                            // Deserialize the edited data into a DataSourceTracker object
                            data = JsonConvert.DeserializeObject<DataSourceTracker>(editor.TextData);

                            // Validate the name and title of the note with the edited data
                            data.ValidateName(note, vm.Workgroup, ref errors);
                            data.ValidateTitle(note, vm.Workgroup, ref errors);

                            // Copy the metadata from the edited data to the note
                            note.Metadata.CopyTo(data);
                            retry = false;
                            Log.Logger.Information("Metadata updated");
                        }
                        catch (ValidationException)
                        {
                            data = null;
                            foreach (var error in errors)
                            {
                                Log.Logger.Error($"Error: {error}");
                                Console.Error.WriteLine($"Error: {error}");
                            }
                            // Prompt the user to try again if validation errors occur
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
                            // Prompt the user to try again if unable to read modified data
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
                // Repeat the edit process if needed
                while (retry);
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to create a new note with a specified name and title
        /// </summary>
        /// <param name="name">The name of the note to create</param>
        /// <param name="title">The title of the note to create</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkCreate(string name = null, string title = null)
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                var wg = vm.Workgroup;

                // If no name is specified, prompt the user to input the name
                if (name == null)
                    name = ReadLineNoteName();

                // Check if a note with the same name already exists and handle it
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

                // If no title is specified, prompt the user to input the title
                if (title == null)
                    title = ReadLineNoteTitle();

                // If the title is empty, set it to the name
                if (string.IsNullOrWhiteSpace(title))
                    title = name;

                // Get the application data directory
                var dir = ConfigurationCli.Instance.ApplicationDataDirectory;
                string path = NoteUtil.GetNotePath(dir, name);

                Note note = null;
                try
                {
                    // Create a new note with the specified name, title, and path
                    note = Note.Create(name, title, path);
                }
                catch (Exception e)
                {
                    Log.Logger.Error(e.Message);
                    Console.WriteLine($"Error: {e.Message}");
                    return -1;
                }

                // Add the created note to the workgroup
                WorkAdd(path);
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
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

        /// <summary>
        /// Method to list all notes in the workgroup with an optional flag to show only completed notes
        /// </summary>
        /// <param name="completion">Flag to indicate if only completed notes should be listed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkList(bool completion = false)
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();
                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();

                // Loop through all notes in the workgroup
                foreach (var note in vm.Workgroup.Notes)
                {
                    if (!completion)
                    {
                        // Check if the current note is selected
                        bool check = note == vm.Workgroup.SelectedNote;
                        // Create a buffer to store the formatted note information
                        StringBuilder buffer = new StringBuilder();
                        // Append a symbol to indicate if the note is selected or not
                        buffer.Append(check ? "*" : " ");
                        buffer.Append(" ");
                        buffer.Append(note.ToString());
                        Console.WriteLine(buffer.ToString());
                    }
                    else
                    {
                        // Display only the name of the note
                        Console.WriteLine(note.Metadata.Name);
                    }
                }

                // Save the configuration changes
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to add a new note to the workgroup using the specified path
        /// </summary>
        /// <param name="path">The path of the note to be added</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
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

                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();

                // Attempt to load the note database
                try
                {
                    using (NoteDbContext db = new NoteDbContext(path)) { }
                }
                catch
                {
                    Console.Error.WriteLine("Error: Failed to load");
                    return -1;
                }

                // Add the path to the list of data sources in the configuration
                if (!ConfigurationCli.Instance.DataSources.Contains(path))
                    ConfigurationCli.Instance.DataSources.Add(path);

                // Add the path to the list of data sources used by the workgroup
                if (!ConfigurationCli.Instance.Workgroup.UseDataSources.Contains(path))
                    ConfigurationCli.Instance.Workgroup.UseDataSources.Add(path);

                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();

                // Save the configuration changes
                ConfigurationCli.Instance.Save();
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to remove a note from the workgroup based on the specified name
        /// </summary>
        /// <param name="name">The name of the note to be removed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
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
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to backup a note in the workgroup
        /// </summary>
        /// <param name="name">Optional parameter specifying the name of the note to backup</param>
        /// <param name="outputPath">Optional parameter specifying the output path for the backup</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkBackup(string name = null, string outputPath = null)
        {
            try
            {
                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();
                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();
                // Declare a variable to hold the current note
                Note current = null;

                // Check if a specific note name is provided
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

                // Check if an output path is provided
                if (outputPath != null)
                {
                    // Check if the directory of the output path exists
                    if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
                    {
                        Console.Error.WriteLine("Error: No such output directory");
                        return -1;
                    }
                    // Check if the output file already exists
                    if (File.Exists(outputPath))
                    {
                        Console.Error.WriteLine("Error: Output file exists");
                        return -1;
                    }
                }
                else
                {
                    // Generate a default output path if none provided
                    outputPath = NoteUtil.GetJsonPath(Environment.CurrentDirectory, current.Metadata.Name);
                }

                // Perform the backup operation
                NoteUtil.Backup(current, outputPath).Wait();

                Console.WriteLine("Backup completed");
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to restore a note in the workgroup from a backup file
        /// </summary>
        /// <param name="inputPath">The path to the backup file to be restored</param>
        /// <param name="outputDir">Optional parameter specifying the output directory for the restored note</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
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

                // Perform the restore operation
                NoteUtil.Restore(inputPath, outputDir).Wait();

                Console.WriteLine("Restore completed");
                return 0;
            }
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to import text files into the workgroup
        /// </summary>
        /// <param name="importDir">The directory containing the text files to import</param>
        /// <param name="recursive">Optional parameter specifying whether to import recursively</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Import(string importDir, bool recursive = false)
        {
            try
            {
                // Check if importDir parameter is null
                if (importDir == null)
                    throw new ArgumentNullException(nameof(importDir));

                // Check if the import directory exists
                if (!Directory.Exists(importDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }

                // Create a new configuration instance
                ConfigurationCli.Instance = ConfigurationCli.Create();
                // Initialize a new MemoriaNoteViewModel
                var vm = new MemoriaNoteViewModel();

                // Import the text files into the workgroup
                NoteUtil.TextImporter(vm.Workgroup.SelectedNote, importDir, recursive).Wait();

                Console.WriteLine("Import completed");
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Method to export a note in the workgroup to a text file
        /// </summary>
        /// <param name="exportDir">The directory where the text file will be exported</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Export(string exportDir)
        {
            try
            {
                // Check if exportDir parameter is null
                if (exportDir == null)
                    throw new ArgumentNullException(nameof(exportDir));

                // Check if the export directory exists
                if (!Directory.Exists(exportDir))
                {
                    Console.Error.WriteLine("Error: No such directory");
                    return -1;
                }

                ConfigurationCli.Instance = ConfigurationCli.Create();
                var vm = new MemoriaNoteViewModel();

                // Export the note to a text file in the specified directory
                NoteUtil.TextExporter(vm.Workgroup.SelectedNote, exportDir).Wait();

                Console.WriteLine("Export completed");
                return 0;
            }
            // Handle any exceptions that may occur
            catch (Exception e)
            {
                // Log the error message and stack trace
                Log.Logger.Fatal(e.Message);
                Log.Logger.Fatal(e.StackTrace);
                Console.Error.WriteLine($"Fatal: {e.Message}");
                return -1;
            }
        }
    }
}