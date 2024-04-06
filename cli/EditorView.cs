using System.IO;
using System.Text;
using System.Reactive.Linq;
using ReactiveUI;
using MemoriaNote.Cli.Editors;

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
        public static void Run(ScreenController sc, MemoriaNoteViewModel vm)
        {
            // Create a new instance of EditorView with the provided ScreenController and MemoriaNoteViewModel,
            // then start the editing process.
            new EditorView(sc, vm).Start();
        }

        public EditorView(ScreenController controller, MemoriaNoteViewModel viewModel)
        {
            Controller = controller;
            ViewModel = viewModel;
        }

        /// <summary>
        /// Method to start the editing process based on the current TextManageType in the ViewModel.
        /// </summary>
        protected void Start()
        {
            // Create a new instance of a terminal editor.
            var editor = TerminalEditorFactory.Create();

            // Check the current editing state in the ViewModel and execute the corresponding method.
            switch (ViewModel.EditingState)
            {
                case TextManageType.Create:
                    // Start the process for creating a new text.
                    OnCreateText(editor);
                    break;
                case TextManageType.Edit:
                    // Start the process for editing an existing text.
                    OnEditText(editor);
                    break;
                case TextManageType.Rename:
                    // Start the process for renaming a text.
                    OnRenameText(editor);
                    break;
                case TextManageType.Delete:
                    // Start the process for deleting a text.
                    OnDeleteText(editor);
                    break;
                default:
                    // Log an error if the editing state is not recognized.
                    Log.Logger.Error("Error: EditingState none");
                    return;
            }

            // Reset the editing state to None after completing the editing process.
            ViewModel.EditingState = TextManageType.None;
        }

        protected void OnCreateText(ITerminalEditor editor)
        {
            if (!ViewModel.CanCreateText(ViewModel.EditingTitle, ViewModel.EditingText))
                if (!EnterName(editor, ViewModel.EditingState))
                    return;

            if (EnterText(editor))
                Observable.Start(() => { }).InvokeCommand(ViewModel, vm => vm.CreateText);
        }

        protected void OnEditText(ITerminalEditor editor)
        {
            if (EnterText(editor))
                Observable.Start(() => { }).InvokeCommand(ViewModel, vm => vm.EditText);
        }

        protected void OnRenameText(ITerminalEditor editor)
        {
            if (EnterName(editor, ViewModel.EditingState))
                Observable.Start(() => { }).InvokeCommand(ViewModel, vm => vm.RenameText);
        }
        
        protected void OnDeleteText(ITerminalEditor editor)
        {
        }

        protected bool EnterName(ITerminalEditor editor, TextManageType type)
        {
            if (type == TextManageType.Rename)
                editor.FileName = "Rename text";
            else if (type == TextManageType.Delete)
                editor.FileName = "Delete text";
            else
                editor.FileName = "New text";

            editor.TextData = AddNameComment(ViewModel.EditingTitle);

            if (!editor.Edit())
            {
                Log.Logger.Information("A name enter canceled");
                ViewModel.ManageNotice = "A name enter canceled";
                return false;
            }

            ViewModel.EditingTitle = RemoveNameComment(editor.TextData);
            return true;
        }

        protected bool EnterText(ITerminalEditor editor)
        {
            editor.FileName = ViewModel.EditingTitle;
            editor.TextData = ViewModel.EditingText;

            if (!editor.Edit())
            {
                Log.Logger.Information("A text enter canceled");
                ViewModel.SearchNotice = "A text enter canceled";
                return false;
            }

            ViewModel.EditingText = editor.TextData;
            return true;
        }

        static string AddNameComment(string name)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendLine(name);
            buffer.AppendLine();
            buffer.AppendLine("#### Please enter a new name ####");
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