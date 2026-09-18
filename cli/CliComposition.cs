using System;
using System.IO;
using System.Globalization;
using MemoriaNote.Cli.Terminal;
using MemoriaNote.Persistence;
using MemoriaNote.Transfer;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MemoriaNote.Cli
{
    /// <summary>Owns process-scoped services composed by the CLI entry point.</summary>
    internal sealed class CliComposition : IDisposable
    {
        readonly Serilog.ILogger _serilogLogger;
        readonly ILoggerFactory _loggerFactory;
        readonly ITemporaryFileStore _temporaryFileStore;
        bool _disposed;

        CliComposition(
            CliCommandHandlers commandHandlers,
            Serilog.ILogger serilogLogger,
            ILoggerFactory loggerFactory,
            ITemporaryFileStore temporaryFileStore)
        {
            CommandHandlers = commandHandlers;
            _serilogLogger = serilogLogger;
            _loggerFactory = loggerFactory;
            _temporaryFileStore = temporaryFileStore;
        }

        /// <summary>Gets the composed CLI command handlers.</summary>
        internal CliCommandHandlers CommandHandlers { get; }

        /// <summary>Creates the default process-scoped CLI services.</summary>
        /// <returns>The composed CLI runtime.</returns>
        internal static CliComposition CreateDefault()
        {
            var clock = SystemClock.Instance;
            var applicationPaths = ApplicationPaths.CreateDefault();
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                ApplicationPaths.ApplicationName);
            Directory.CreateDirectory(temporaryDirectory);

            var temporaryFileStore = new TemporaryFileStore(temporaryDirectory);
            var logPath = Path.Combine(
                temporaryDirectory,
                "Log-" + clock.UtcNow.ToString(
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture) + ".txt");
            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("MemoriaNote", Serilog.Events.LogEventLevel.Debug)
                .Enrich.FromLogContext()
                .WriteTo.File(logPath)
                .CreateLogger();
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("MemoriaNote", LogLevel.Debug)
                    .AddSerilog(serilogLogger, dispose: false);
            });

            var databaseFactory = new SqliteNotebookDbContextFactory(loggerFactory);
            var pageRepository = new SqlitePageRepository(databaseFactory, clock);
            var pageSearchRepository = new SqlitePageSearchRepository(databaseFactory);
            var transferRepository = new SqliteNotebookTransferRepository(databaseFactory);
            var metadataRepository = new SqliteNotebookMetadataRepository(databaseFactory);
            var notebookMigrator = new SqliteNotebookMigrator(
                databaseFactory,
                metadataRepository);
            var filePathFactory = new NotebookFilePathFactory(clock);
            var serializer = new JsonConfigurationSerializer<ConfigurationCli>();
            var configurationStore = new FileConfigurationStore<ConfigurationCli>(
                applicationPaths.ConfigurationPath,
                serializer,
                () => ConfigurationCli.CreateDefault(applicationPaths),
                clock);
            var editorExecutableResolver = new Editors.EditorExecutableResolver(
                new Editors.ProcessEnvironmentVariableSource());
            var editorFileExchange = new Editors.EditorFileExchange(temporaryFileStore);
            var editorProcessRunner = new Editors.ExternalEditorProcessRunner(
                loggerFactory.CreateLogger<Editors.ExternalEditorProcessRunner>());
            var externalEditor = new Editors.ExternalEditor(
                editorExecutableResolver,
                editorFileExchange,
                editorProcessRunner);
            var output = new ConsoleCommandOutput(Console.Out, Console.Error);
            var input = new ConsoleCommandInput(Console.In);
            var prompt = new CommandPrompt(input, output);
            var errorMapper = new CliErrorMapper();
            var executor = new CliCommandExecutor(
                output,
                errorMapper,
                loggerFactory.CreateLogger<CliCommandExecutor>());
            var contextFactory = new CliCommandContextFactory(
                notebookMigrator,
                pageRepository,
                pageSearchRepository,
                metadataRepository,
                applicationPaths,
                configurationStore,
                output,
                loggerFactory);
            var searchQueryNormalizer = new CliSearchQueryNormalizer();
            var textPageImporter = new TextPageImporter(pageRepository);
            var textPageExporter = new TextPageExporter(transferRepository);
            var backupService = new NotebookBackupService(
                transferRepository,
                metadataRepository,
                notebookMigrator,
                filePathFactory);

            var workAdd = new WorkAddCommandHandler(
                executor,
                contextFactory,
                databaseFactory);
            var commandHandlers = new CliCommandHandlers(
                new FindCommandHandler(executor),
                new EditCommandHandler(executor, contextFactory, externalEditor, output),
                new NewCommandHandler(executor, contextFactory, externalEditor, output),
                new ConfigEditCommandHandler(
                    executor,
                    contextFactory,
                    serializer,
                    applicationPaths,
                    externalEditor,
                    prompt,
                    output,
                    loggerFactory.CreateLogger<ConfigEditCommandHandler>()),
                new ConfigShowCommandHandler(
                    executor,
                    contextFactory,
                    serializer,
                    output),
                new ListCommandHandler(
                    executor,
                    contextFactory,
                    searchQueryNormalizer,
                    output),
                new WorkSelectCommandHandler(executor, contextFactory),
                new WorkListCommandHandler(executor, contextFactory, output),
                new WorkCreateCommandHandler(
                    executor,
                    contextFactory,
                    notebookMigrator,
                    filePathFactory,
                    applicationPaths,
                    workAdd,
                    prompt,
                    output),
                new WorkEditCommandHandler(
                    executor,
                    contextFactory,
                    externalEditor,
                    prompt,
                    output,
                    loggerFactory.CreateLogger<WorkEditCommandHandler>()),
                workAdd,
                new WorkRemoveCommandHandler(executor, contextFactory),
                new WorkBackupCommandHandler(
                    executor,
                    contextFactory,
                    backupService,
                    filePathFactory,
                    output),
                new WorkRestoreCommandHandler(
                    executor,
                    contextFactory,
                    backupService,
                    output),
                new ImportCommandHandler(
                    executor,
                    contextFactory,
                    textPageImporter,
                    output),
                new ExportCommandHandler(
                    executor,
                    contextFactory,
                    textPageExporter,
                    output));

            return new CliComposition(
                commandHandlers,
                serilogLogger,
                loggerFactory,
                temporaryFileStore);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _loggerFactory.Dispose();
            (_serilogLogger as IDisposable)?.Dispose();
            _temporaryFileStore.Dispose();
            _disposed = true;
        }
    }
}
