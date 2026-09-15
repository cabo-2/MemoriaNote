using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli.Editors
{
    internal sealed class PageEditorWorkflow : IPageEditorWorkflow
    {
        readonly IExternalEditor _externalEditor;
        readonly ILogger<PageEditorWorkflow> _logger;

        internal PageEditorWorkflow(
            IExternalEditor externalEditor,
            ILogger<PageEditorWorkflow> logger)
        {
            _externalEditor = externalEditor ??
                throw new ArgumentNullException(nameof(externalEditor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RunAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));

            switch (viewModel.EditingState)
            {
                case EditorMode.Create:
                    await CreatePageAsync(viewModel, cancellationToken);
                    break;
                case EditorMode.Edit:
                    await EditPageAsync(viewModel, cancellationToken);
                    break;
                case EditorMode.Rename:
                    await RenamePageAsync(viewModel, cancellationToken);
                    break;
                case EditorMode.Delete:
                    await DeletePageAsync(viewModel, cancellationToken);
                    break;
                default:
                    _logger.LogError("Error: EditingState none");
                    return;
            }

            viewModel.EditingState = EditorMode.None;
        }

        async Task CreatePageAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            if (!await viewModel.CanCreateTextAsync(
                viewModel.EditingTitle,
                viewModel.EditingText,
                cancellationToken))
            {
                if (!await EnterNameAsync(
                    viewModel,
                    EditorMode.Create,
                    cancellationToken))
                {
                    return;
                }
            }

            if (await EnterTextAsync(viewModel, cancellationToken))
                await viewModel.CreateTextHandler();
        }

        async Task EditPageAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            if (await EnterTextAsync(viewModel, cancellationToken))
                await viewModel.EditTextHandler();
        }

        async Task RenamePageAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            if (await EnterNameAsync(
                viewModel,
                EditorMode.Rename,
                cancellationToken))
            {
                await viewModel.RenameTextHandler();
            }
        }

        async Task DeletePageAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            if (await EnterNameAsync(
                viewModel,
                EditorMode.Delete,
                cancellationToken))
            {
                await viewModel.DeleteTextHandler();
            }
        }

        async Task<bool> EnterNameAsync(
            MemoriaNoteViewModel viewModel,
            EditorMode mode,
            CancellationToken cancellationToken)
        {
            var fileName = mode switch
            {
                EditorMode.Rename => "Rename text",
                EditorMode.Delete => "Delete text",
                _ => "New text"
            };
            var result = await _externalEditor.EditAsync(
                viewModel.Configuration,
                new ExternalEditorDocument(
                    fileName,
                    PageEditorNameDocument.Create(viewModel.EditingTitle, mode)),
                cancellationToken);
            if (!result.IsChanged)
            {
                _logger.LogInformation("A name enter canceled");
                viewModel.ManageNotice = "A name enter canceled";
                return false;
            }

            viewModel.EditingTitle = PageEditorNameDocument.ReadName(result.Text);
            return true;
        }

        async Task<bool> EnterTextAsync(
            MemoriaNoteViewModel viewModel,
            CancellationToken cancellationToken)
        {
            var result = await _externalEditor.EditAsync(
                viewModel.Configuration,
                new ExternalEditorDocument(
                    viewModel.EditingTitle,
                    viewModel.EditingText),
                cancellationToken);
            if (!result.IsChanged)
            {
                _logger.LogInformation("A text enter canceled");
                viewModel.SearchNotice = "A text enter canceled";
                return false;
            }

            viewModel.EditingText = result.Text;
            return true;
        }
    }
}
