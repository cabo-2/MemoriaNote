using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{

    /// <summary>
    /// Represents a view for editing text in a terminal screen.
    /// Implements the ITerminalScreen interface.
    /// </summary>
    public class EditorView : ITerminalScreen
    {
        /// <summary>
        /// Method to initialize and run the EditorView with the provided ScreenController and MemoriaNoteViewModel.
        /// </summary>
        /// <param name="sc">The ScreenController to be used.</param>
        /// <param name="vm">The MemoriaNoteViewModel to be used.</param>
        /// <param name="terminalEditorFactory">Creates the configured external editor.</param>
        /// <param name="logger">The presentation logger.</param>
        /// <param name="cancellationToken">Cancels the editing operation.</param>
        /// <returns>A task representing the editing operation.</returns>
        public static Task RunAsync(
            ScreenController sc,
            MemoriaNoteViewModel vm,
            TerminalEditorFactory terminalEditorFactory,
            ILogger<EditorView> logger,
            CancellationToken cancellationToken)
        {
            // Create a new instance of EditorView with the provided ScreenController and MemoriaNoteViewModel,
            // then start the editing process.
            return new EditorView(sc, vm, terminalEditorFactory, logger)
                .StartAsync(cancellationToken);
        }

        readonly TerminalEditorFactory _terminalEditorFactory;
        readonly ILogger<EditorView> _logger;

        public EditorView(
            ScreenController controller,
            MemoriaNoteViewModel viewModel,
            TerminalEditorFactory terminalEditorFactory,
            ILogger<EditorView> logger)
        {
            Controller = controller;
            ViewModel = viewModel;
            _terminalEditorFactory = terminalEditorFactory ??
                throw new System.ArgumentNullException(nameof(terminalEditorFactory));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Method to start the editing process based on the current EditorMode in the ViewModel.
        /// </summary>
        protected async Task StartAsync(CancellationToken cancellationToken)
        {
            // Create a new instance of a terminal editor.
            var editor = _terminalEditorFactory.Create(ViewModel.Configuration);

            // Check the current editing state in the ViewModel and execute the corresponding method.
            switch (ViewModel.EditingState)
            {
                case EditorMode.Create:
                    // Start the process for creating a new text.
                    await OnCreateTextAsync(editor, cancellationToken);
                    break;
                case EditorMode.Edit:
                    // Start the process for editing an existing text.
                    await OnEditTextAsync(editor, cancellationToken);
                    break;
                case EditorMode.Rename:
                    // Start the process for renaming a text.
                    await OnRenameTextAsync(editor, cancellationToken);
                    break;
                case EditorMode.Delete:
                    // Start the process for deleting a text.
                    await OnDeleteTextAsync(editor, cancellationToken);
                    break;
                default:
                    // Log an error if the editing state is not recognized.
                    _logger.LogError("Error: EditingState none");
                    return;
            }

            // Reset the editing state to None after completing the editing process.
            ViewModel.EditingState = EditorMode.None;
        }

        protected async Task OnCreateTextAsync(
            ITerminalEditor editor,
            CancellationToken cancellationToken)
        {
            if (!await ViewModel.CanCreateTextAsync(
                ViewModel.EditingTitle,
                ViewModel.EditingText,
                cancellationToken))
            {
                if (!await EnterNameAsync(
                    editor,
                    ViewModel.EditingState,
                    cancellationToken))
                {
                    return;
                }
            }

            if (await EnterTextAsync(editor, cancellationToken))
                await ViewModel.CreateTextHandler();
        }

        protected async Task OnEditTextAsync(
            ITerminalEditor editor,
            CancellationToken cancellationToken)
        {
            if (await EnterTextAsync(editor, cancellationToken))
                await ViewModel.EditTextHandler();
        }

        protected async Task OnRenameTextAsync(
            ITerminalEditor editor,
            CancellationToken cancellationToken)
        {
            if (await EnterNameAsync(
                editor,
                EditorMode.Rename,
                cancellationToken))
            {
                await ViewModel.RenameTextHandler();
            }
        }
        
        protected async Task OnDeleteTextAsync(
            ITerminalEditor editor,
            CancellationToken cancellationToken)
        {
            if (await EnterNameAsync(
                editor,
                EditorMode.Delete,
                cancellationToken))
            {
                await ViewModel.DeleteTextHandler();
            }
        }

        protected async Task<bool> EnterNameAsync(
            ITerminalEditor editor,
            EditorMode type,
            CancellationToken cancellationToken)
        {
            if (type == EditorMode.Rename)
                editor.FileName = "Rename text";
            else if (type == EditorMode.Delete)
                editor.FileName = "Delete text";
            else
                editor.FileName = "New text";

            editor.TextData = AddNameComment(ViewModel.EditingTitle, type);

            if (!await editor.EditAsync(cancellationToken))
            {
                _logger.LogInformation("A name enter canceled");
                ViewModel.ManageNotice = "A name enter canceled";
                return false;
            }

            ViewModel.EditingTitle = RemoveNameComment(editor.TextData);
            return true;
        }

        protected async Task<bool> EnterTextAsync(
            ITerminalEditor editor,
            CancellationToken cancellationToken)
        {
            editor.FileName = ViewModel.EditingTitle;
            editor.TextData = ViewModel.EditingText;

            if (!await editor.EditAsync(cancellationToken))
            {
                _logger.LogInformation("A text enter canceled");
                ViewModel.SearchNotice = "A text enter canceled";
                return false;
            }

            ViewModel.EditingText = editor.TextData;
            return true;
        }

        static string AddNameComment(string name, EditorMode type)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendLine(name);
            buffer.AppendLine();

            if (type == EditorMode.Rename)
                buffer.AppendLine("#### Enter the name to be renamed ####");
            else if (type == EditorMode.Delete)
                buffer.AppendLine("#### Enter the name to be deleted ####");
            else
                buffer.AppendLine("#### Enter a name to be created ####");
            return buffer.ToString();
        }

        static string RemoveNameComment(string name)
        {
            using (StringReader reader = new StringReader(name))
            {
                string line = reader.ReadLine();
                while (line != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        break;

                    line = reader.ReadLine();
                }
                if (line != null && line.Length > 0 && line[0] != '#')
                    return line;
                else
                    return string.Empty;
            }
        }

        /// <summary>
        /// Gets or sets the ScreenController used in the EditorView.
        /// </summary>
        public ScreenController Controller { get; set; }

        /// <summary>
        /// Gets or sets the MemoriaNoteViewModel used in the EditorView.
        /// </summary>
        public MemoriaNoteViewModel ViewModel { get; set; }
    }
}
