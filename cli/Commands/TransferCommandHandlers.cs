using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Transfer;

namespace MemoriaNote.Cli
{
    internal sealed class ImportCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly TextPageImporter _textPageImporter;
        readonly ICommandOutput _output;

        internal ImportCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            TextPageImporter textPageImporter,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _textPageImporter = textPageImporter ??
                throw new ArgumentNullException(nameof(textPageImporter));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string importDirectory,
            bool recursive,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (importDirectory == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No import directory");
                }
                if (!Directory.Exists(importDirectory))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such directory");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                await _textPageImporter.ImportAsync(
                    NotebookId.FromDatabasePath(
                        session.Workspace.SelectedNotebook.DatabasePath),
                    importDirectory,
                    recursive,
                    token);
                _output.WriteLine("Import completed");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }

    internal sealed class ExportCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly TextPageExporter _textPageExporter;
        readonly ICommandOutput _output;

        internal ExportCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            TextPageExporter textPageExporter,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _textPageExporter = textPageExporter ??
                throw new ArgumentNullException(nameof(textPageExporter));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string exportDirectory,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                if (exportDirectory == null)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "No export directory");
                }
                if (!Directory.Exists(exportDirectory))
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.NotFound,
                        "No such directory");
                }

                var configuration = _contextFactory.LoadConfiguration();
                var session = await _contextFactory.CreateSessionAsync(
                    configuration,
                    token);
                await _textPageExporter.ExportAsync(
                    NotebookId.FromDatabasePath(
                        session.Workspace.SelectedNotebook.DatabasePath),
                    exportDirectory,
                    token);
                _output.WriteLine("Export completed");
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
