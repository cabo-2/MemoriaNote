using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Persistence;
using MemoriaNote.Transfer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MemoriaNote.Cli
{
    internal sealed class WorkSelectCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;

        internal WorkSelectCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
        }

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (name == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No note name");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var workspace = session.Workspace;
                if (!workspace.Notebooks.Any(
                    notebook => name == notebook.Metadata.Name))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such note");
                }

                configuration.Workspace.SelectedNotebookName = name;
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class WorkEditCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly IExternalEditor _externalEditor;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;
        readonly ILogger<WorkEditCommandHandler> _logger;
        readonly NotebookMetadataValidationPolicy _validationPolicy;

        internal WorkEditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IExternalEditor externalEditor,
            CommandPrompt prompt,
            ICommandOutput output,
            ILogger<WorkEditCommandHandler> logger)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validationPolicy = new NotebookMetadataValidationPolicy();
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var notebook = session.Workspace.SelectedNotebook;
                bool retry;
                do
                {
                    retry = false;
                    var document = NotebookMetadataEditDocument.Create(notebook.Metadata);
                    var editResult = await _externalEditor.EditAsync(
                        configuration,
                        new ExternalEditorDocument(
                            notebook.ToString(),
                            JsonConvert.SerializeObject(
                                document,
                                Formatting.Indented)),
                        token);

                    if (editResult.IsChanged)
                    {
                        try
                        {
                            document = JsonConvert
                                .DeserializeObject<NotebookMetadataEditDocument>(
                                    editResult.Text) ??
                                throw new JsonException(
                                    "The metadata editor did not contain an object.");
                            var update = document.ToUpdate();
                            var errors = _validationPolicy.Validate(
                                NotebookId.FromDatabasePath(notebook.DatabasePath),
                                update,
                                session.Workspace);
                            if (errors.Count > 0)
                            {
                                foreach (var error in errors)
                                {
                                    var message = NotebookMetadataValidationMessageMapper
                                        .ToErrorMessage(error);
                                    _logger.LogError(
                                        "Error: {ValidationError}",
                                        message);
                                    _output.WriteErrorLine($"Error: {message}");
                                }

                                if (_prompt.ReadTryAgain())
                                {
                                    retry = true;
                                    continue;
                                }

                                return CliCommandResult.Failure(
                                    CliErrorKind.Validation,
                                    NotebookMetadataValidationMessageMapper
                                        .ToErrorMessage(errors[0]),
                                    alreadyReported: true);
                            }

                            await notebook.UpdateMetadataAsync(
                                NotebookMetadataPatch.Create(
                                    notebook.Metadata,
                                    update),
                                token);
                            _logger.LogInformation("Metadata updated");
                        }
                        catch
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            _output.WriteErrorLine("Error: Unable to read modified data");
                            if (_prompt.ReadTryAgain())
                                retry = true;
                            else
                            {
                                return CliCommandResult.Failure(
                                    CliErrorKind.Validation,
                                    "Unable to read modified data",
                                    alreadyReported: true);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Metadata edit canceled");
                        _output.WriteLine("Operation was canceled");
                    }
                } while (retry);

                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal WorkCreateCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            INotebookMigrator notebookMigrator,
            NotebookFilePathFactory notebookFilePathFactory,
            ApplicationPaths applicationPaths,
            WorkAddCommandHandler workAddCommandHandler,
            CommandPrompt prompt,
            ICommandOutput output)
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
        }

        internal Task<int> ExecuteAsync(
            string name,
            string title,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var workspace = session.Workspace;
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
                        {
                            return CliCommandResult.Failure(
                                CliErrorKind.Conflict,
                                "A note with that name already exists",
                                alreadyReported: true);
                        }
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
                    await _notebookMigrator.CreateAsync(
                        name,
                        title,
                        path,
                        token);
                }
                catch (ArgumentException exception) when (File.Exists(path))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        exception.Message,
                        exception);
                }
                catch (ArgumentException exception)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        exception.Message,
                        exception);
                }

                return _workAddCommandHandler.Add(path, token);
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            bool completion,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                if (completion)
                {
                    _output.WriteNotebookCompletion(session.Workspace.Notebooks);
                }
                else
                {
                    _output.WriteNotebookList(
                        session.Workspace.Notebooks,
                        session.Workspace.SelectedNotebook);
                }

                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class WorkAddCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly INotebookDbContextFactory _databaseFactory;

        internal WorkAddCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            INotebookDbContextFactory databaseFactory)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        internal Task<int> ExecuteAsync(
            string path,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                token => Add(path, token),
                cancellationToken);
        }

        internal CliCommandResult Add(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path == null)
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "No path");
            }
            if (!File.Exists(path))
            {
                return CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    "No such file");
            }

            var configuration = _contextFactory.LoadConfiguration();
            try
            {
                using var database = _databaseFactory.CreateDbContext(path);
            }
            catch
            {
                return CliCommandResult.Failure(
                    CliErrorKind.Storage,
                    "Failed to load");
            }

            if (!configuration.DataSources.Contains(path))
                configuration.DataSources.Add(path);
            if (!configuration.Workspace.NotebookDatabasePaths.Contains(path))
                configuration.Workspace.NotebookDatabasePaths.Add(path);
            _contextFactory.SaveConfiguration(configuration);
            return CliCommandResult.Success();
        }
    }

    internal sealed class WorkRemoveCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;

        internal WorkRemoveCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
        }

        internal Task<int> ExecuteAsync(
            string name,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (name == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No note name");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var workspace = session.Workspace;
                if (!workspace.Notebooks.Any(
                    notebook => name == notebook.Metadata.Name))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such remove note");
                }
                if (workspace.Notebooks.Count == 1)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Conflict,
                        "Cannot remove the last note");
                }

                var dataSource = workspace.Notebooks
                    .First(notebook => name == notebook.Metadata.Name)
                    .DatabasePath;
                configuration.Workspace.NotebookDatabasePaths.Remove(dataSource);
                _contextFactory.SaveConfiguration(configuration);
                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            string name,
            string outputPath,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                var current = name switch
                {
                    null => session.Workspace.SelectedNotebook,
                    _ => session.Workspace.Notebooks.FirstOrDefault(
                        notebook => notebook.Metadata.Name == name)
                };
                if (current == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such name");
                }

                if (outputPath != null)
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(directory))
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.NotFound,
                            "No such output directory");
                    }
                    if (File.Exists(outputPath))
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.Conflict,
                            "Output file exists");
                    }
                }
                else
                {
                    outputPath = _notebookFilePathFactory.CreateBackupPath(
                        Environment.CurrentDirectory,
                        current.Metadata.Name);
                }

                await _backupService.CreateBackupAsync(
                    NotebookId.FromDatabasePath(current.DatabasePath),
                    outputPath,
                    token);
                _output.WriteLine("Backup completed");
                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(
            string inputPath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (inputPath == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No input file");
                }
                if (!File.Exists(inputPath))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such input file");
                }

                _contextFactory.LoadConfiguration();
                if (outputDirectory != null)
                {
                    if (!Directory.Exists(outputDirectory))
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.NotFound,
                            "No such directory");
                    }
                }
                else
                {
                    outputDirectory = Environment.CurrentDirectory;
                }

                await _backupService.RestoreBackupAsync(
                    inputPath,
                    outputDirectory,
                    token);
                _output.WriteLine("Restore completed");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
