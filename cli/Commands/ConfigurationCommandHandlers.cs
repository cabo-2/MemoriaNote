using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        readonly IExternalEditor _externalEditor;
        readonly CommandPrompt _prompt;
        readonly ICommandOutput _output;
        readonly ILogger<ConfigEditCommandHandler> _logger;

        internal ConfigEditCommandHandler(
            CliCommandExecutor executor,
            ICliCommandContextFactory contextFactory,
            IConfigurationSerializer<ConfigurationCli> serializer,
            ApplicationPaths applicationPaths,
            IExternalEditor externalEditor,
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
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var configuration = _contextFactory.LoadConfiguration();
                bool retry;
                do
                {
                    retry = false;
                    var editResult = await _externalEditor.EditAsync(
                        configuration,
                        null,
                        new ExternalEditorDocument(
                            Path.GetFileName(_applicationPaths.ConfigurationPath),
                            _serializer.Serialize(configuration)),
                        token);

                    if (editResult.IsChanged)
                    {
                        try
                        {
                            configuration = _serializer.Deserialize(editResult.Text);
                        }
                        catch (ConfigurationFormatException)
                        {
                            _logger.LogError("Error: Unable to read modified data");
                            _output.WriteErrorLine("Error: Unable to read modified data");
                            if (!_prompt.ReadTryAgain())
                            {
                                return CliCommandResult.Failure(
                                    CliErrorKind.Validation,
                                    "Unable to read modified data",
                                    alreadyReported: true);
                            }

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

                return CliCommandResult.Success();
            }, cancellationToken);
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

        internal Task<int> ExecuteAsync(CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                var configuration = _contextFactory.LoadConfiguration();
                var serializedConfiguration = _serializer.Serialize(configuration);
                using var reader = new StringReader(serializedConfiguration);
                string line;
                while ((line = reader.ReadLine()) != null)
                    _output.WriteLine(line);
                return CliCommandResult.Success();
            }, cancellationToken);
        }
    }
}
