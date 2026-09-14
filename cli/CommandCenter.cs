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
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the command center for managing various actions within the application.
    /// </summary>
    public class CommandCenter
    {
        readonly INotebookMigrator _notebookMigrator;
        readonly INotebookDbContextFactory _databaseFactory;
        readonly IPageRepository _pageRepository;
        readonly IPageSearchRepository _pageSearchRepository;
        readonly INotebookMetadataRepository _metadataRepository;
        readonly TextPageImporter _textPageImporter;
        readonly TextPageExporter _textPageExporter;
        readonly NotebookBackupService _notebookBackupService;
        readonly NotebookFilePathFactory _notebookFilePathFactory;
        readonly ApplicationPaths _applicationPaths;
        readonly IConfigurationSerializer<ConfigurationCli> _configurationSerializer;
        readonly IConfigurationStore<ConfigurationCli> _configurationStore;
        readonly Editors.TerminalEditorFactory _terminalEditorFactory;
        readonly ILoggerFactory _loggerFactory;
        readonly ILogger<CommandCenter> _logger;
        readonly CliSearchQueryNormalizer _searchQueryNormalizer =
            new CliSearchQueryNormalizer();

        /// <summary>
        /// Initializes a command center with explicitly composed runtime services.
        /// </summary>
        /// <param name="notebookMigrator">The service used for note database lifecycle operations.</param>
        /// <param name="databaseFactory">The shared notebook database context factory.</param>
        /// <param name="pageRepository">The page persistence service.</param>
        /// <param name="pageSearchRepository">The page search persistence service.</param>
        /// <param name="transferRepository">The notebook transfer persistence service.</param>
        /// <param name="metadataRepository">The notebook metadata persistence service.</param>
        /// <param name="notebookFilePathFactory">The notebook file path factory.</param>
        /// <param name="applicationPaths">The application paths.</param>
        /// <param name="configurationSerializer">The CLI configuration serializer.</param>
        /// <param name="configurationStore">The CLI configuration store.</param>
        /// <param name="terminalEditorFactory">The terminal editor factory.</param>
        /// <param name="loggerFactory">The shared logger factory.</param>
        public CommandCenter(
            INotebookMigrator notebookMigrator,
            INotebookDbContextFactory databaseFactory,
            IPageRepository pageRepository,
            IPageSearchRepository pageSearchRepository,
            INotebookTransferRepository transferRepository,
            INotebookMetadataRepository metadataRepository,
            NotebookFilePathFactory notebookFilePathFactory,
            ApplicationPaths applicationPaths,
            IConfigurationSerializer<ConfigurationCli> configurationSerializer,
            IConfigurationStore<ConfigurationCli> configurationStore,
            Editors.TerminalEditorFactory terminalEditorFactory,
            ILoggerFactory loggerFactory)
        {
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _pageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
            _pageSearchRepository = pageSearchRepository ??
                throw new ArgumentNullException(nameof(pageSearchRepository));
            _metadataRepository = metadataRepository ??
                throw new ArgumentNullException(nameof(metadataRepository));
            _notebookFilePathFactory = notebookFilePathFactory ??
                throw new ArgumentNullException(nameof(notebookFilePathFactory));
            _textPageImporter = new TextPageImporter(_pageRepository);
            _textPageExporter = new TextPageExporter(transferRepository);
            _notebookBackupService = new NotebookBackupService(
                transferRepository,
                _metadataRepository,
                notebookMigrator,
                _notebookFilePathFactory);
            _applicationPaths = applicationPaths ??
                throw new ArgumentNullException(nameof(applicationPaths));
            _configurationSerializer = configurationSerializer ??
                throw new ArgumentNullException(nameof(configurationSerializer));
            _configurationStore = configurationStore ??
                throw new ArgumentNullException(nameof(configurationStore));
            _terminalEditorFactory = terminalEditorFactory ??
                throw new ArgumentNullException(nameof(terminalEditorFactory));
            _loggerFactory = loggerFactory ??
                throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<CommandCenter>();
        }

        ConfigurationCli LoadConfiguration()
        {
            var result = _configurationStore.Load();
            if (result.Status == ConfigurationLoadStatus.RecoveredInvalid)
            {
                Console.Error.WriteLine(
                    "Warning: The configuration file was invalid and was moved to " +
                    $"\"{result.RecoveryArtifactPath}\". " +
                    "Default configuration has been created.");
            }

            return result.Configuration;
        }

        MemoriaNoteViewModel CreateViewModel(ConfigurationCli configuration)
        {
            var request = new ApplicationStartupRequest(
                configuration.DefaultNotebookName,
                configuration.DefaultNotebookTitle,
                _applicationPaths.DefaultNotebookDatabasePath,
                configuration.DataSources);
            var startupService = new ApplicationStartupService(
                _notebookMigrator,
                new FileNotebookDatabaseProbe(),
                new ConfiguredWorkspaceLoader(
                    configuration.Workspace,
                    _pageRepository,
                    _pageSearchRepository,
                    _metadataRepository));
            var session = startupService.StartAsync(request, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (session.DefaultNotebookCreated)
                _logger.LogInformation("Default note created");

            return new MemoriaNoteViewModel(
                configuration,
                session,
                _loggerFactory.CreateLogger<MemoriaNoteService>());
        }

        ScreenController CreateScreenController()
        {
            return new ScreenController(_terminalEditorFactory, _loggerFactory);
        }

        /// <summary>
        /// 共通の前後処理＋例外ハンドリングを行うラッパー
        /// </summary>
        private int Execute(Func<int> body)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                // 例外はすべてログ＆標準エラー出力
                _logger.LogCritical(e, "A CLI command failed unexpectedly.");
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
                var configuration = LoadConfiguration();

                var vm = CreateViewModel(configuration);
                vm.SearchEntry = _searchQueryNormalizer.Normalize(name);
                vm.SearchRange = configuration.State.SearchRange;
                vm.SearchMethod = configuration.State.SearchMethod;

                var sc = CreateScreenController();
                sc.RequestHome();
                sc.Start(vm);

                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to edit a specific entry by name
        /// </summary>
        /// <param name="name">The name of the entry to be edited</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int Edit(string name = null)
            => Execute(() =>
            {
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                vm.SearchEntry = name;
                vm.SearchRange = SearchRangeType.Notebook;
                vm.SearchMethod = SearchMethodType.Heading;
                var sc = CreateScreenController();
                sc.RequestManage();
                sc.Start(vm);
                _configurationStore.Save(configuration);
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
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                vm.SearchEntry = name;
                vm.SearchRange = SearchRangeType.Notebook;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.EditingTitle = name;
                vm.EditingState = EditorMode.Create;
                var sc = CreateScreenController();
                sc.RequestManage();
                sc.RequestEditor();
                sc.Start(vm);
                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to edit the configuration settings
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int ConfigEdit()
            => Execute(() =>
            {
                var configuration = LoadConfiguration();
                bool retry;
                do
                {
                    retry = false;
                    var editor = _terminalEditorFactory.Create(configuration);
                    editor.FileName = Path.GetFileName(_applicationPaths.ConfigurationPath);
                    editor.TextData = _configurationSerializer.Serialize(configuration);

                    if (editor.Edit())
                    {
                        try
                        {
                            configuration = _configurationSerializer.Deserialize(
                                editor.TextData);
                        }
                        catch (ConfigurationFormatException)
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (!ReadLineTryAgain())
                                return -1;

                            retry = true;
                            continue;
                        }

                        _configurationStore.Save(configuration);
                        _logger.LogInformation("Configuration updated");
                    }
                    else
                    {
                        _logger.LogInformation("Configuration edit canceled");
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
                var configuration = LoadConfiguration();
                var serializedConfiguration =
                    _configurationSerializer.Serialize(configuration);
                using var reader = new StringReader(serializedConfiguration);
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
                var configuration = LoadConfiguration();
                if (completion && configuration.Terminal.Completion == CompletionType.None)
                    return 0;

                var vm = CreateViewModel(configuration);
                vm.SearchEntry = _searchQueryNormalizer.Normalize(name);
                vm.SearchRange = SearchRangeType.Notebook;
                vm.SearchMethod = SearchMethodType.Heading;
                vm.ActivateHandler().Wait();

                if (completion)
                    WriteLineCompletion(vm.Contents, vm.ContentsCount);
                else
                    WriteLineList(vm.Contents, vm.ContentsCount);

                _configurationStore.Save(configuration);
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

        static int GetMaxNameLength(List<PageSummary> list)
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

        static void WriteLineList(List<PageSummary> contents, int totalCount)
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
                buffer.Append(content.PageId.Value.ToHashId());
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

        static void WriteLineCompletion(List<PageSummary> contents, int totalCount)
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
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                var workspace = vm.Workspace;
                if (!workspace.Notebooks.Any(n => name == n.Metadata.Name))
                {
                    Console.Error.WriteLine("Error: No such note");
                    return -1;
                }
                configuration.Workspace.SelectedNotebookName = name;
                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to edit the metadata of a selected note
        /// </summary>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkEdit()
            => Execute(() =>
            {
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                var note = vm.Workspace.SelectedNotebook;
                bool retry;
                do
                {
                    retry = false;
                    var data = DataSourceTracker.Create(note.Metadata);
                    var errors = new List<string>();
                    var editor = _terminalEditorFactory.Create(configuration);
                    editor.FileName = note.ToString();
                    editor.TextData = JsonConvert.SerializeObject(data, Formatting.Indented);

                    if (editor.Edit())
                    {
                        try
                        {
                            data = JsonConvert.DeserializeObject<DataSourceTracker>(editor.TextData);
                            data.ValidateName(note, vm.Workspace, ref errors);
                            data.ValidateTitle(note, vm.Workspace, ref errors);
                            note.UpdateMetadata(
                                NotebookMetadataPatch.Create(note.Metadata, data));
                            _logger.LogInformation("Metadata updated");
                        }
                        catch (ValidationException)
                        {
                            foreach (var err in errors)
                            {
                                _logger.LogError("Error: {ValidationError}", err);
                                Console.Error.WriteLine($"Error: {err}");
                            }
                            if (ReadLineTryAgain()) retry = true;
                            else return -1;
                        }
                        catch
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            Console.Error.WriteLine("Error: Unable to read modified data");
                            if (ReadLineTryAgain()) retry = true;
                            else return -1;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Metadata edit canceled");
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
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                var workspace = vm.Workspace;
                if (name == null) name = ReadLineNoteName();
                bool retry;
                do
                {
                    retry = false;
                    if (workspace.Notebooks.Any(n => name == n.Metadata.Name))
                    {
                        Console.Error.WriteLine("Error: A note with that name already exists");
                        if (!ReadLineTryAgain()) return -1;
                        name = ReadLineNoteName();
                        retry = true;
                    }
                } while (retry);
                if (title == null) title = ReadLineNoteTitle();
                if (string.IsNullOrWhiteSpace(title)) title = name;
                var path = _notebookFilePathFactory.CreateDatabasePath(
                    _applicationPaths.ApplicationDataDirectory,
                    name);
                try
                {
                    _notebookMigrator.CreateAsync(
                            name,
                            title,
                            path,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Notebook creation failed.");
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
        /// Method to list all notes in the workspace with an optional flag to show only completed notes
        /// </summary>
        /// <param name="completion">Flag to indicate if only completed notes should be listed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkList(bool completion = false)
            => Execute(() =>
            {
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                foreach (var note in vm.Workspace.Notebooks)
                {
                    if (!completion)
                    {
                        var mark = note == vm.Workspace.SelectedNotebook ? "*" : " ";
                        Console.WriteLine($"{mark} {note}");
                    }
                    else
                    {
                        Console.WriteLine(note.Metadata.Name);
                    }
                }
                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to add a new note to the workspace using the specified path
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
                var configuration = LoadConfiguration();
                try
                {
                    using var db = _databaseFactory.CreateDbContext(path);
                }
                catch
                {
                    Console.Error.WriteLine("Error: Failed to load");
                    return -1;
                }
                if (!configuration.DataSources.Contains(path))
                    configuration.DataSources.Add(path);
                if (!configuration.Workspace.NotebookDatabasePaths.Contains(path))
                    configuration.Workspace.NotebookDatabasePaths.Add(path);
                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to remove a note from the workspace based on the specified name
        /// </summary>
        /// <param name="name">The name of the note to be removed</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkRemove(string name)
            => Execute(() =>
            {
                if (name == null) throw new ArgumentNullException(nameof(name));
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                var workspace = vm.Workspace;
                if (!workspace.Notebooks.Any(n => name == n.Metadata.Name))
                {
                    Console.Error.WriteLine("Error: No such remove note");
                    return -1;
                }
                if (workspace.Notebooks.Count == 1)
                {
                    Console.Error.WriteLine("Error: Cannot remove the last note");
                    return -1;
                }
                var ds = workspace.Notebooks.First(n => name == n.Metadata.Name).DatabasePath;
                configuration.Workspace.NotebookDatabasePaths.Remove(ds);
                _configurationStore.Save(configuration);
                return 0;
            });

        /// <summary>
        /// Method to backup a note in the workspace
        /// </summary>
        /// <param name="name">Optional parameter specifying the name of the note to backup</param>
        /// <param name="outputPath">Optional parameter specifying the output path for the backup</param>
        /// <returns>0 if successful, -1 if an exception occurs</returns>
        public int WorkBackup(string name = null, string outputPath = null)
            => Execute(() =>
            {
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                Notebook current = name switch
                {
                    null => vm.Workspace.SelectedNotebook,
                    _ => vm.Workspace.Notebooks.FirstOrDefault(n => n.Metadata.Name == name)
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
                    outputPath = _notebookFilePathFactory.CreateBackupPath(
                        Environment.CurrentDirectory,
                        current.Metadata.Name);
                }
                _notebookBackupService.CreateBackupAsync(
                        NotebookId.FromDatabasePath(current.DatabasePath),
                        outputPath,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Console.WriteLine("Backup completed");
                return 0;
            });

        /// <summary>
        /// Method to restore a note in the workspace from a backup file
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
                LoadConfiguration();
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
                _notebookBackupService.RestoreBackupAsync(
                        inputPath,
                        outputDir,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Console.WriteLine("Restore completed");
                return 0;
            });

        /// <summary>
        /// Method to import text files into the workspace
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
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                _textPageImporter.ImportAsync(
                        NotebookId.FromDatabasePath(
                            vm.Workspace.SelectedNotebook.DatabasePath),
                        importDir,
                        recursive,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Console.WriteLine("Import completed");
                return 0;
            });

        /// <summary>
        /// Method to export a note in the workspace to a text file
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
                var configuration = LoadConfiguration();
                var vm = CreateViewModel(configuration);
                _textPageExporter.ExportAsync(
                        NotebookId.FromDatabasePath(
                            vm.Workspace.SelectedNotebook.DatabasePath),
                        exportDir,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Console.WriteLine("Export completed");
                return 0;
            });
    }
}
