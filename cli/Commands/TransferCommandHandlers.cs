using System;
using System.IO;
using System.Threading;

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

        internal int Execute(string importDirectory, bool recursive = false)
        {
            return _executor.Execute(() =>
            {
                if (importDirectory == null)
                    throw new ArgumentNullException(nameof(importDirectory));
                if (!Directory.Exists(importDirectory))
                {
                    _output.WriteErrorLine("Error: No such directory");
                    return -1;
                }

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                _textPageImporter.ImportAsync(
                        NotebookId.FromDatabasePath(
                            viewModel.Workspace.SelectedNotebook.DatabasePath),
                        importDirectory,
                        recursive,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                _output.WriteLine("Import completed");
                return 0;
            });
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

        internal int Execute(string exportDirectory)
        {
            return _executor.Execute(() =>
            {
                if (exportDirectory == null)
                    throw new ArgumentNullException(nameof(exportDirectory));
                if (!Directory.Exists(exportDirectory))
                {
                    _output.WriteErrorLine("Error: No such directory");
                    return -1;
                }

                var configuration = _contextFactory.LoadConfiguration();
                var viewModel = _contextFactory.CreateViewModel(configuration);
                _textPageExporter.ExportAsync(
                        NotebookId.FromDatabasePath(
                            viewModel.Workspace.SelectedNotebook.DatabasePath),
                        exportDirectory,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                _output.WriteLine("Export completed");
                return 0;
            });
        }
    }
}
