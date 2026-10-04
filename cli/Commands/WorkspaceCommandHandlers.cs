using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;
using MemoriaNote.Persistence;
using MemoriaNote.Transfer;

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

        internal WorkEditCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The 'mn work edit' command is no longer supported. " +
                    "Use 'mn notebooks metadata [--notebook <notebook>] " +
                    "[--name <value> | --title <value> | --description <value> | " +
                    "--author <value> | --tag <value> | " +
                    "--read-only <true|false>]' instead."),
                cancellationToken);
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

        internal WorkBackupCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(
            string name,
            string outputPath,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The 'mn work backup' command is no longer supported. " +
                    "Use 'mn notebooks backup [<archive>] " +
                    "[--notebook <notebook>]' instead."),
                cancellationToken);
        }
    }

    internal sealed class WorkRestoreCommandHandler
    {
        readonly CliCommandExecutor _executor;

        internal WorkRestoreCommandHandler(CliCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        internal Task<int> ExecuteAsync(
            string inputPath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(
                _ => CliCommandResult.Failure(
                    CliErrorKind.Validation,
                    "The 'mn work restore' command is no longer supported. " +
                    "Use 'mn notebooks restore [<archive>] --target <notebook> " +
                    "[--dry-run]' instead."),
                cancellationToken);
        }
    }
}
