using System;
using System.IO;
using System.Globalization;
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
            CommandCenter commandCenter,
            Serilog.ILogger serilogLogger,
            ILoggerFactory loggerFactory,
            ITemporaryFileStore temporaryFileStore)
        {
            CommandCenter = commandCenter;
            _serilogLogger = serilogLogger;
            _loggerFactory = loggerFactory;
            _temporaryFileStore = temporaryFileStore;
        }

        /// <summary>Gets the composed command center.</summary>
        internal CommandCenter CommandCenter { get; }

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
            var editorFactory = new Editors.TerminalEditorFactory(
                temporaryFileStore,
                loggerFactory);
            var commandCenter = new CommandCenter(
                notebookMigrator,
                databaseFactory,
                pageRepository,
                pageSearchRepository,
                transferRepository,
                metadataRepository,
                filePathFactory,
                applicationPaths,
                serializer,
                configurationStore,
                editorFactory,
                loggerFactory);

            return new CliComposition(
                commandCenter,
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
