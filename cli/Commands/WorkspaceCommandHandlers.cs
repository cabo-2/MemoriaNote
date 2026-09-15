using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MemoriaNote.Cli
{
    internal sealed class WorkSelectCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly ICommandOutput _output;

        internal WorkSelectCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string name)
        {
            return _executor.Execute(() =>
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                var workspace = viewModel.Workspace;
                if (!workspace.Notebooks.Any(
                    notebook => name == notebook.Metadata.Name))
                {
                    _output.WriteErrorLine("Error: No such note");
                    return -1;
                }

                configuration.Workspace.SelectedNotebookName = name;
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class WorkEditCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly TerminalEditorFactory _terminalEditorFactory;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;
        readonly ILogger<WorkEditCommandHandler> _logger;

        internal WorkEditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            TerminalEditorFactory terminalEditorFactory,
            CommandPrompt prompt,
            ICommandOutput output,
            ILogger<WorkEditCommandHandler> logger)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _terminalEditorFactory = terminalEditorFactory ??
                throw new ArgumentNullException(nameof(terminalEditorFactory));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal int Execute()
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                var notebook = viewModel.Workspace.SelectedNotebook;
                bool retry;
                do
                {
                    retry = false;
                    var data = DataSourceTracker.Create(notebook.Metadata);
                    var errors = new List<string>();
                    var editor = _terminalEditorFactory.Create(configuration);
                    editor.FileName = notebook.ToString();
                    editor.TextData = JsonConvert.SerializeObject(
                        data,
                        Formatting.Indented);

                    if (editor.Edit())
                    {
                        try
                        {
                            data = JsonConvert.DeserializeObject<DataSourceTracker>(
                                editor.TextData);
                            data.ValidateName(
                                notebook,
                                viewModel.Workspace,
                                ref errors);
                            data.ValidateTitle(
                                notebook,
                                viewModel.Workspace,
                                ref errors);
                            notebook.UpdateMetadata(
                                NotebookMetadataPatch.Create(notebook.Metadata, data));
                            _logger.LogInformation("Metadata updated");
                        }
                        catch (ValidationException)
                        {
                            foreach (var error in errors)
                            {
                                _logger.LogError(
                                    "Error: {ValidationError}",
                                    error);
                                _output.WriteErrorLine($"Error: {error}");
                            }

                            if (_prompt.ReadTryAgain())
                                retry = true;
                            else
                                return -1;
                        }
                        catch
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            _output.WriteErrorLine("Error: Unable to read modified data");
                            if (_prompt.ReadTryAgain())
                                retry = true;
                            else
                                return -1;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Metadata edit canceled");
                        _output.WriteLine("Operation was canceled");
                    }
                } while (retry);

                return 0;
            });
        }
    }

    internal sealed class WorkCreateCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly INotebookMigrator _notebookMigrator;
        readonly NotebookFilePathFactory _notebookFilePathFactory;
        readonly ApplicationPaths _applicationPaths;
        readonly WorkAddCommandHandler _workAddCommandHandler;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;
        readonly ILogger<WorkCreateCommandHandler> _logger;

        internal WorkCreateCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            INotebookMigrator notebookMigrator,
            NotebookFilePathFactory notebookFilePathFactory,
            ApplicationPaths applicationPaths,
            WorkAddCommandHandler workAddCommandHandler,
            CommandPrompt prompt,
            ICommandOutput output,
            ILogger<WorkCreateCommandHandler> logger)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _notebookMigrator = notebookMigrator ??
                throw new ArgumentNullException(nameof(notebookMigrator));
            _notebookFilePathFactory = notebookFilePathFactory ??
                throw new ArgumentNullException(nameof(notebookFilePathFactory));
            _applicationPaths = applicationPaths ??
                throw new ArgumentNullException(nameof(applicationPaths));
            _workAddCommandHandler = workAddCommandHandler ??
                throw new ArgumentNullException(nameof(workAddCommandHandler));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal int Execute(string name = null, string title = null)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                var workspace = viewModel.Workspace;
                if (name == null)
                    name = _prompt.ReadNotebookName();

                bool retry;
                do
                {
                    retry = false;
                    if (workspace.Notebooks.Any(
                        notebook => name == notebook.Metadata.Name))
                    {
                        _output.WriteErrorLine(
                            "Error: A note with that name already exists");
                        if (!_prompt.ReadTryAgain())
                            return -1;
                        name = _prompt.ReadNotebookName();
                        retry = true;
                    }
                } while (retry);

                if (title == null)
                    title = _prompt.ReadNotebookTitle();
                if (string.IsNullOrWhiteSpace(title))
                    title = name;

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
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Notebook creation failed.");
                    _output.WriteLine($"Error: {exception.Message}");
                    return -1;
                }

                _workAddCommandHandler.Execute(path);
                return 0;
            });
        }
    }

    internal sealed class WorkListCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly ICommandOutput _output;

        internal WorkListCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(bool completion = false)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                if (completion)
                {
                    _output.WriteNotebookCompletion(viewModel.Workspace.Notebooks);
                }
                else
                {
                    _output.WriteNotebookList(
                        viewModel.Workspace.Notebooks,
                        viewModel.Workspace.SelectedNotebook);
                }

                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class WorkAddCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly INotebookDbContextFactory _databaseFactory;
        readonly ICommandOutput _output;

        internal WorkAddCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            INotebookDbContextFactory databaseFactory,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string path)
        {
            return _executor.Execute(() =>
            {
                if (path == null)
                    throw new ArgumentNullException(nameof(path));
                if (!File.Exists(path))
                {
                    _output.WriteErrorLine("Error: No such file");
                    return -1;
                }

                var configuration = _contextFactory.LoadConfiguration();
                try
                {
                    using var database = _databaseFactory.CreateDbContext(path);
                }
                catch
                {
                    _output.WriteErrorLine("Error: Failed to load");
                    return -1;
                }

                if (!configuration.DataSources.Contains(path))
                    configuration.DataSources.Add(path);
                if (!configuration.Workspace.NotebookDatabasePaths.Contains(path))
                    configuration.Workspace.NotebookDatabasePaths.Add(path);
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class WorkRemoveCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly ICommandOutput _output;

        internal WorkRemoveCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string name)
        {
            return _executor.Execute(() =>
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                var workspace = viewModel.Workspace;
                if (!workspace.Notebooks.Any(
                    notebook => name == notebook.Metadata.Name))
                {
                    _output.WriteErrorLine("Error: No such remove note");
                    return -1;
                }
                if (workspace.Notebooks.Count == 1)
                {
                    _output.WriteErrorLine("Error: Cannot remove the last note");
                    return -1;
                }

                var dataSource = workspace.Notebooks
                    .First(notebook => name == notebook.Metadata.Name)
                    .DatabasePath;
                configuration.Workspace.NotebookDatabasePaths.Remove(dataSource);
                _contextFactory.SaveConfiguration(configuration);
                return 0;
            });
        }
    }

    internal sealed class WorkBackupCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly NotebookBackupService _backupService;
        readonly NotebookFilePathFactory _notebookFilePathFactory;
        readonly ICommandOutput _output;

        internal WorkBackupCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            NotebookBackupService backupService,
            NotebookFilePathFactory notebookFilePathFactory,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _backupService = backupService ??
                throw new ArgumentNullException(nameof(backupService));
            _notebookFilePathFactory = notebookFilePathFactory ??
                throw new ArgumentNullException(nameof(notebookFilePathFactory));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string name = null, string outputPath = null)
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                var current = name switch
                {
                    null => viewModel.Workspace.SelectedNotebook,
                    _ => viewModel.Workspace.Notebooks.FirstOrDefault(
                        notebook => notebook.Metadata.Name == name)
                };
                if (current == null)
                {
                    _output.WriteErrorLine("Error: No such name");
                    return -1;
                }

                if (outputPath != null)
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(directory))
                    {
                        _output.WriteErrorLine("Error: No such output directory");
                        return -1;
                    }
                    if (File.Exists(outputPath))
                    {
                        _output.WriteErrorLine("Error: Output file exists");
                        return -1;
                    }
                }
                else
                {
                    outputPath = _notebookFilePathFactory.CreateBackupPath(
                        Environment.CurrentDirectory,
                        current.Metadata.Name);
                }

                _backupService.CreateBackupAsync(
                        NotebookId.FromDatabasePath(current.DatabasePath),
                        outputPath,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                _output.WriteLine("Backup completed");
                return 0;
            });
        }
    }

    internal sealed class WorkRestoreCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly NotebookBackupService _backupService;
        readonly ICommandOutput _output;

        internal WorkRestoreCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            NotebookBackupService backupService,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _backupService = backupService ??
                throw new ArgumentNullException(nameof(backupService));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute(string inputPath, string outputDirectory = null)
        {
            return _executor.Execute(() =>
            {
                if (inputPath == null)
                    throw new ArgumentNullException(nameof(inputPath));
                if (!File.Exists(inputPath))
                {
                    _output.WriteErrorLine("Error: No such input file");
                    return -1;
                }

                _contextFactory.LoadConfiguration();
                if (outputDirectory != null)
                {
                    if (!Directory.Exists(outputDirectory))
                    {
                        _output.WriteErrorLine("Error: No such directory");
                        return -1;
                    }
                }
                else
                {
                    outputDirectory = Environment.CurrentDirectory;
                }

                _backupService.RestoreBackupAsync(
                        inputPath,
                        outputDirectory,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                _output.WriteLine("Restore completed");
                return 0;
            });
        }
    }
}
