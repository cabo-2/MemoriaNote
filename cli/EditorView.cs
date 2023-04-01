using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using Terminal.Gui;
using ReactiveMarbles.ObservableEvents;
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
            //ViewModel.EditingTitle = null;
            //ViewModel.EditingText = null;
            ViewModel.EditingState = TextManageType.None;
            Controller.RequestManage();
        }

        protected void OnCreateText(ITerminalEditor editor)
        {         
            editor.FileName = "Enter a name";
            editor.TextData = AddNameComment(ViewModel.EditingTitle);

            if (!editor.Edit())
            {
                Log.Logger.Information("Enter a name canceled");
                return;
            }

            ViewModel.EditingTitle = RemoveNameComment(editor.TextData);

            editor.FileName = ViewModel.EditingTitle;
            editor.TextData = ViewModel.EditingText;

            if (!editor.Edit())
            {
                Log.Logger.Information("Enter a text canceled");
                return;
            }

            ViewModel.EditingText = editor.TextData;

            Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.CreateText);
        }

        protected void OnEditText(ITerminalEditor editor)
        {
            editor.FileName = ViewModel.EditingTitle;
            editor.TextData = ViewModel.EditingText;

            if (!editor.Edit())
            {
                Log.Logger.Information("Enter a text canceled");
                return;
            }

            ViewModel.EditingText = editor.TextData;

            Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.EditText);
        }
        protected void OnRenameText(ITerminalEditor editor)
        {
            editor.FileName = "Enter a name";
            editor.TextData = AddNameComment(ViewModel.EditingTitle);

            if (!editor.Edit())
            {
                Log.Logger.Information("Enter a name canceled");
                return;
            }

            ViewModel.EditingTitle = RemoveNameComment(editor.TextData);           

            Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.RenameText);
        }
        protected void OnDeleteText(ITerminalEditor editor)
        {
        }

        static string AddNameComment(string name)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendLine(name);
            buffer.AppendLine();
            buffer.AppendLine("#### Please enter a text name ####");
            return buffer.ToString();
        }

        static string RemoveNameComment(string name)
        {
            using(StringReader reader = new StringReader(name))
            {
                string line = reader.ReadLine();
                while(line != null)
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