using System;
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

        public EditorView (ScreenController controller, MemoriaNoteViewModel viewModel)
        {
            Controller = controller;
            ViewModel = viewModel;            
        }

        protected void Start()
        {
            var editor = TerminalEditorFactory.Create();
            editor.Name = ViewModel.EditingTitle?.ToString();
            editor.Text = ViewModel.EditingText?.ToString();
            if (editor.Edit()) 
            {
                var note = ViewModel.Workgroup.SelectedNote;
                if (ViewModel.EditingState == EditingState.Create)
                {
                    note.Write(editor.Name, editor.Text);
                    ViewModel.Notification = "Editor text created";
                    Log.Logger.Debug("Editor text created");
                }
                else if (ViewModel.EditingState == EditingState.Update)
                {
                    var page = ViewModel.OpenedPage;
                    if (page != null) {
                        page.Text = editor.Text;
                        note.Rewrite(page);
                        ViewModel.Notification = "Editor text updated";
                        Log.Logger.Debug("Editor text updated");
                    }
                    else
                        throw new ArgumentException(nameof(Start));
                }
                else
                {
                    Log.Logger.Debug("unknown editing state? " + ViewModel.EditingState.ToString());
                }
            }            
            else
            {
                ViewModel.Notification = "Editor canceled";
                Log.Logger.Debug("Editor canceled");
            }

            ViewModel.EditingTitle = null;
            ViewModel.EditingText = null;
            ViewModel.EditingState = EditingState.None;
        }

        public ScreenController Controller { get; set; }
        public MemoriaNoteViewModel ViewModel { get; set; }
    }
}