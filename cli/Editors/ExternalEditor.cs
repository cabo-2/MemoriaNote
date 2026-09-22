using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote.Cli.Editors
{
    internal sealed class ExternalEditor : IExternalEditor
    {
        readonly EditorExecutableResolver _executableResolver;
        readonly IEditorFileExchange _fileExchange;
        readonly IExternalEditorProcessRunner _processRunner;

        internal ExternalEditor(
            EditorExecutableResolver executableResolver,
            IEditorFileExchange fileExchange,
            IExternalEditorProcessRunner processRunner)
        {
            _executableResolver = executableResolver ??
                throw new ArgumentNullException(nameof(executableResolver));
            _fileExchange = fileExchange ??
                throw new ArgumentNullException(nameof(fileExchange));
            _processRunner = processRunner ??
                throw new ArgumentNullException(nameof(processRunner));
        }

        public Task<ExternalEditorResult> EditAsync(
            ConfigurationCli configuration,
            ExternalEditorCommand commandOverride,
            ExternalEditorDocument document,
            CancellationToken cancellationToken)
        {
            var command = _executableResolver.Resolve(configuration, commandOverride);
            return _fileExchange.EditAsync(
                document,
                (documentPath, token) => _processRunner.RunAsync(
                    command,
                    documentPath,
                    token),
                cancellationToken);
        }
    }
}
