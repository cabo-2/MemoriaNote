using System;
using System.IO;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    internal sealed class ConfigEditCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly IConfigurationSerializer<ConfigurationCli> _serializer;
        readonly ApplicationPaths _applicationPaths;
        readonly TerminalEditorFactory _terminalEditorFactory;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;
        readonly ILogger<ConfigEditCommandHandler> _logger;

        internal ConfigEditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IConfigurationSerializer<ConfigurationCli> serializer,
            ApplicationPaths applicationPaths,
            TerminalEditorFactory terminalEditorFactory,
            CommandPrompt prompt,
            ICommandOutput output,
            ILogger<ConfigEditCommandHandler> logger)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _applicationPaths = applicationPaths ??
                throw new ArgumentNullException(nameof(applicationPaths));
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
                bool retry;
                do
                {
                    retry = false;
                    var editor = _terminalEditorFactory.Create(configuration);
                    editor.FileName = Path.GetFileName(_applicationPaths.ConfigurationPath);
                    editor.TextData = _serializer.Serialize(configuration);

                    if (editor.Edit())
                    {
                        try
                        {
                            configuration = _serializer.Deserialize(editor.TextData);
                        }
                        catch (ConfigurationFormatException)
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            _output.WriteErrorLine("Error: Unable to read modified data");
                            if (!_prompt.ReadTryAgain())
                                return -1;

                            retry = true;
                            continue;
                        }

                        _contextFactory.SaveConfiguration(configuration);
                        _logger.LogInformation("Configuration updated");
                    }
                    else
                    {
                        _logger.LogInformation("Configuration edit canceled");
                        _output.WriteLine("Operation was canceled");
                    }
                } while (retry);

                return 0;
            });
        }
    }

    internal sealed class ConfigShowCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly ICliCommandContextFactory _contextFactory;
        readonly IConfigurationSerializer<ConfigurationCli> _serializer;
        readonly ICommandOutput _output;

        internal ConfigShowCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IConfigurationSerializer<ConfigurationCli> serializer,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _contextFactory = contextFactory ??
                throw new ArgumentNullException(nameof(contextFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal int Execute()
        {
            return _executor.Execute(() =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                var serializedConfiguration = _serializer.Serialize(configuration);
                using var reader = new StringReader(serializedConfiguration);
                string line;
                while ((line = reader.ReadLine()) != null)
                    _output.WriteLine(line);
                return 0;
            });
        }
    }
}
