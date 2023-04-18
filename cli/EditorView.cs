using System.IO;
using System.Text;
using System.Reactive.Linq;
using ReactiveUI;
using MemoriaNote.Cli.Editors;

namespace MemoriaNote.Cli
{

    public class EditorView : ITerminalScreen
    {
        public static void Run(ScreenController sc, MemoriaNoteViewModel vm)
        {
            new EditorView(sc, vm).Start();
        }

        public EditorView(ScreenController controller, MemoriaNoteViewModel viewModel)
        {
            Controller = controller;
            ViewModel = viewModel;
        }

        protected void Start()
        {
            var editor = TerminalEditorFactory.Create();
            switch (ViewModel.EditingState)
            {
                case TextManageType.Create:
                    OnCreateText(editor);
                    break;
                case TextManageType.Edit:
                    OnEditText(editor);
                    break;
                case TextManageType.Rename:
                    OnRenameText(editor);
                    break;
                case TextManageType.Delete:
                    OnDeleteText(editor);
                    break;
                default:
                    Log.Logger.Error("Error: EditingState none");
                    return;
            }
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

        public ScreenController Controller { get; set; }
        public MemoriaNoteViewModel ViewModel { get; set; }
    }
}