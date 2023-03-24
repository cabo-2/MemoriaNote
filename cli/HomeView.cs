using System;
using System.Text;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Collections.Generic;
using ReactiveUI;
using Terminal.Gui;
using ReactiveMarbles.ObservableEvents;
using System.Reactive.Concurrency;

namespace MemoriaNote.Cli
{

    public class HomeView : Toplevel, IViewFor<MemoriaNoteViewModel>, ITerminalScreen, IDisposable
    {
        public static void Run(ScreenController sc, MemoriaNoteViewModel vm)
        {
            Application.Init();
            RxApp.MainThreadScheduler = TerminalScheduler.Default;
            RxApp.TaskpoolScheduler = TaskPoolScheduler.Default;
            Application.Run(new HomeView(sc, vm));
            Application.Shutdown();
        }

        readonly CompositeDisposable _disposable = new CompositeDisposable();

        protected FrameView _navigation;
        protected FrameView _contentsFrame;
        protected FrameView _editorFrame;
        protected ColorScheme _colorScheme;

        const int NotesLabelWidth = 6;
        const int NotesWidth = 30;
        const int SearchTextLabelWidth = 8;
        const int SearchTextWidth = 20;
        const int NotifyLabelWidth = 8;
        const int NotifyWidth = 30;
        const int ContentPosX = 4;
        const int ContentWidth = 25;
        const int EditorPosX = ContentWidth;

        static readonly StringBuilder aboutMessage;
        static HomeView()
        {
            aboutMessage = new StringBuilder();
            aboutMessage.AppendLine(@"");
            aboutMessage.AppendLine(@" __  __                           _       ");
            aboutMessage.AppendLine(@"|  \/  | ___ _ __ ___   ___  _ __(_) __ _ ");
            aboutMessage.AppendLine(@"| |\/| |/ _ \ '_ ` _ \ / _ \| '__| |/ _` |");
            aboutMessage.AppendLine(@"| |  | |  __/ | | | | | (_) | |  | | (_| |");
            aboutMessage.AppendLine(@"|_|  |_|\___|_| |_| |_|\___/|_|  |_|\__,_|");
            aboutMessage.AppendLine(@"  _   _       _           ____ _     ___  ");
            aboutMessage.AppendLine(@" | \ | | ___ | |_ ___    / ___| |   |_ _| ");
            aboutMessage.AppendLine(@" |  \| |/ _ \| __/ _ \  | |   | |    | |  ");
            aboutMessage.AppendLine(@" | |\  | (_) | ||  __/  | |___| |___ | |  ");
            aboutMessage.AppendLine(@" |_| \_|\___/ \__\___|   \____|_____|___| ");
            aboutMessage.AppendLine(@"");
            aboutMessage.AppendLine(@"https://github.com/gui-cs/Terminal.Gui");
        }

        public HomeView(ScreenController controller, MemoriaNoteViewModel viewModel)
        {
            Controller = controller;
            ViewModel = viewModel;

            CreateMenuBar();
            _navigation = CreateNavigation();
            _contentsFrame = CreateContentsFrame();
            _editorFrame = CreateEditorFrame();
            CreateStatusBar();

            Add(MenuBar);
            Add(_navigation);
            Add(_contentsFrame);
            Add(_editorFrame);
            Add(StatusBar);

            this.Events()
                .Loaded
                .InvokeCommand(ViewModel, vm => vm.Activate)
                .DisposeWith(_disposable);
        }

        protected MenuBar CreateMenuBar()
        {
            ColorScheme = _colorScheme = Colors.Base;
            MenuBar = new MenuBar(new MenuBarItem[] {
                    new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem ("_Quit", "Exit", () => RequestStop(), null, null, Key.Q | Key.CtrlMask)
                    }),
                    new MenuBarItem ("_Work", CreateNoteMenuItems()),
                    new MenuBarItem ("_Theme", CreateColorSchemeMenuItems()),
                    new MenuBarItem ("_Help", new MenuItem [] {
                        new MenuItem ("_About...",
                            "About", () =>  MessageBox.Query ("About", aboutMessage.ToString(), "_Ok"), null, null, Key.CtrlMask | Key.A)
                    }),
                });
            return MenuBar;
        }

        protected MenuItem[] CreateNoteMenuItems()
        {
            var items = new List<MenuItem>();            
            var notes = ViewModel.Workgroup.Notes.ToArray();
            var index = ViewModel.Workgroup.SelectedNoteIndex;
            var length = Math.Min(notes.Length, 10);

            for (int i=0; i<length; i++) {
                var isCurrent = (i == index);
                var item = new MenuItem() {
                    Title = notes[i].ToString(),
                    Help = "",                    
                    Checked = isCurrent,
                    Shortcut = NumberToKey(i) | Key.CtrlMask | Key.AltMask,
                    Action = () => {
                        Log.Logger.Debug("Push NoteItem: " + notes[i].ToString());
                    }
                };
                items.Add(item);
            }
            return items.ToArray();
        }

        static Key NumberToKey(int number) {           
            switch(number) {
                case 0: return Key.D0;
                case 1: return Key.D1;
                case 2: return Key.D2;
                case 3: return Key.D3;
                case 4: return Key.D4;
                case 5: return Key.D5;
                case 6: return Key.D6;
                case 7: return Key.D7;
                case 8: return Key.D8;
                case 9: return Key.D9;
                default: throw new ArgumentException(nameof(number));
            }
        }

        protected StatusBar CreateStatusBar()
        {
            StatusBar = new StatusBar()
            {
                Visible = true,
                AutoSize = true
            };
            StatusBar.Items = new StatusItem[] {
                    new StatusItem(Key.Q | Key.CtrlMask, "~CTRL-Q~ Quit", () => {
                        Application.RequestStop ();
                    }),
                    new StatusItem(Key.Null," ",() => {}),
                    new StatusItem(Key.F1, "~F1~ Prev  ", () => {
                        Log.Logger.Debug("Push F1 Function");
                        Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.PagePrev);                       
                    }),
                    new StatusItem(Key.F2, "~F2~ Next  ", () => {
                        Log.Logger.Debug("Push F2 Function");
                        Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.PageNext);
                    }),
                    new StatusItem(Key.Null," ",() => {}),
                    new StatusItem(Key.F5, "~F5~ WildCard  ", () => {                        
                                               //RegularExp"
                        Log.Logger.Debug("Push F5 Function");
                    }),
                    new StatusItem(Key.F6, "~F6~ " + ViewModel.SearchRangeString, () => {
                        Log.Logger.Debug("Push F6 Function");                    
                        if (ViewModel.SearchRange == SearchRangeType.Note)
                            ViewModel.SearchRange = SearchRangeType.Workgroup;
                        else
                            ViewModel.SearchRange = SearchRangeType.Note;

                        Controller.RequestHome();
                        Application.RequestStop ();
                        Log.Logger.Debug("Search range changed: " + ViewModel.SearchRangeString); 
                    }),
                    new StatusItem(Key.F7, "~F7~ " + ViewModel.SearchMethodString, () => {
                                               //Full text
                        Log.Logger.Debug("Push F7 Function");                  
                        if (ViewModel.SearchMethod == SearchMethodType.Headline)
                            ViewModel.SearchMethod = SearchMethodType.FullText;
                        else
                            ViewModel.SearchMethod = SearchMethodType.Headline;
                        
                        Controller.RequestHome();
                        Application.RequestStop ();
                        Log.Logger.Debug("Search method changed: " + ViewModel.SearchMethodString); 
                    }),
                    new StatusItem(Key.Null," ",() => {}),
                    new StatusItem(Key.F9, "~F9~ Browse Mode", () => {
                        Log.Logger.Debug("Push F9 Browse Mode");
                    })
                };
            return StatusBar;
        }

        protected FrameView CreateNavigation()
        {
            var navigation = new FrameView("Navigation")
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = 3,
                CanFocus = false
            };
            var notesLabel = new Label("Note:")
            {
                X = 1,
                Y = 0,
                Width = NotesLabelWidth,
                Height = 1
            };
            var notesView = new ListView()
            {
                X = Pos.Right(notesLabel),
                Y = 0,
                Width = NotesWidth,
                Height = 1,
                CanFocus = false
            };
            notesView.SetSource(ViewModel.Notes);
            notesView.SelectedItem = ViewModel.SelectedNoteIndex;

            var searchTextLabel = new Label("Search:")
            {
                X = Pos.Right(notesView) + 2,
                Y = 0,
                Width = SearchTextLabelWidth,
                Height = 1
            };
            var searchTextField = new TextField(ViewModel.SearchEntry ?? "")
            {
                X = Pos.Right(searchTextLabel),
                Y = 0,
                Width = SearchTextWidth,
                Height = 1,
                CanFocus = true,
            };
            ViewModel
                .WhenAnyValue(vm => vm.SearchEntry)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(searchTextField, x => x.Text)
                .DisposeWith(_disposable);
            searchTextField.TextChanged += (e) =>
            {
                ViewModel.SearchEntry = searchTextField.Text.ToString();
                Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.Search); 
            };
            searchTextField.ShortcutAction = () =>
            {
                Log.Logger.Debug("New test : " + ViewModel.Contents?.Count.ToString() ?? "null");
                if (ViewModel.Contents.Count == 0)
                {
                    ViewModel.EditingState = EditingState.Create;
                    ViewModel.EditingTitle = searchTextField.Text;
                    ViewModel.EditingText = null;
                    Controller.RequestHome();
                    Controller.RequestEditor();
                    this.RequestStop();
                }
                else
                {
                    ViewModel.Notification = "Duplicate registration is not allowed";
                }
            };

            navigation.Add(notesLabel);
            navigation.Add(notesView);

            navigation.Add(searchTextLabel, searchTextField);

            var notifyLabel = new Label("Notify:")
            {
                X = Pos.Right(searchTextField) + 3,
                Y = 0,
                Width = NotifyLabelWidth,
                Height = 1
            };
            var notifyField = new Label()
            {
                X = Pos.Right(notifyLabel),
                Y = 0,
                Width = NotifyWidth,
                Height = 1,
                CanFocus = false
            };
            ViewModel
                .WhenAnyValue(vm => vm.Notification)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(notifyField, x => x.Text)
                .DisposeWith(_disposable);
            navigation.Add(notifyLabel, notifyField);

            return navigation;
        }

        protected FrameView CreateContentsFrame()
        {
            var contentsFrame = new FrameView("List")
            {
                X = 0,
                Y = ContentPosX,
                Width = ContentWidth,
                Height = Dim.Fill(1),
                CanFocus = false,
            };

            var contentsLabel = new Label()
            {
                X = 0,
                Y = 0,
                Width = ContentWidth,
                Height = 1,
                TextAlignment = TextAlignment.Centered
            };
            ViewModel
                .WhenAnyValue(vm => vm.PlaceHolder)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(contentsLabel, x => x.Text)
                .DisposeWith(_disposable);

            var pagePrevButton = new Button(" <<< ")
            {
                X = 0,
                Y = 1,
                Width = 7,
                Height = 1,
                CanFocus = false
            };
			pagePrevButton
				.Events ()
				.Clicked
				.InvokeCommand (ViewModel, x => x.PagePrev)
				.DisposeWith (_disposable);
            var pageNextButton = new Button(" >>> ")
            {
                X = ContentWidth - 7 - 4,
                Y = 1,
                Width = 7,
                Height = 1,
                CanFocus = false
            };
			pageNextButton
				.Events ()
				.Clicked
				.InvokeCommand (ViewModel, x => x.PageNext)
				.DisposeWith (_disposable);                
            contentsFrame.Add(contentsLabel);
            contentsFrame.Add(pagePrevButton);
            contentsFrame.Add(pageNextButton);

            var contentsListView = new ListView()
            {
                X = 0,
                Y = 2,
                Width = Dim.Fill(0),
                Height = Dim.Fill(0),
                AllowsMarking = false,
                CanFocus = true,
            };
            contentsListView.SelectedItemChanged += (e) =>
            {
                ViewModel.ContentsViewPageIndex = (ViewModel.ContentsViewPageIndex.Item1, e.Item);
                Observable.Start(()=>{}).InvokeCommand(ViewModel,vm => vm.OpenText); 
            };
            contentsListView.SetSource(ViewModel.ContentViewItems);
            contentsFrame.Add(contentsListView);

            var scrollBar = new ScrollBarView(contentsListView, true);
            scrollBar.ChangedPosition += () =>
            {
                contentsListView.TopItem = scrollBar.Position;
                if (contentsListView.TopItem != scrollBar.Position)
                {
                    scrollBar.Position = contentsListView.TopItem;
                }
                contentsListView.SetNeedsDisplay();
            };
            scrollBar.OtherScrollBarView.ChangedPosition += () =>
            {
                contentsListView.LeftItem = scrollBar.OtherScrollBarView.Position;
                if (contentsListView.LeftItem != scrollBar.OtherScrollBarView.Position)
                {
                    scrollBar.OtherScrollBarView.Position = contentsListView.LeftItem;
                }
                contentsListView.SetNeedsDisplay();
            };
            contentsListView.DrawContent += (e) =>
            {
                scrollBar.Size = contentsListView.Source.Count - 1;
                scrollBar.Position = contentsListView.TopItem;
                scrollBar.OtherScrollBarView.Size = contentsListView.Maxlength - 1;
                scrollBar.OtherScrollBarView.Position = contentsListView.LeftItem;
                scrollBar.Refresh();
            };

            return contentsFrame;
        }

        protected FrameView CreateEditorFrame()
        {
            var editorFrame = new FrameView("Text")
            {
                X = ContentWidth,
                Y = ContentPosX,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                CanFocus = true,
            };

            var titleField = new TextField()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                CanFocus = false,
                ReadOnly = true
            };
            ViewModel
                .WhenAnyValue(vm => vm.EditingTitle)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(titleField, x => x.Text)
                .DisposeWith(_disposable);
            editorFrame.Add(titleField);
            var textEditor = new TextEditor()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                BottomOffset = 1,
                RightOffset = 1,
                CanFocus = true,
                ReadOnly = true,
            };
            ViewModel
                .WhenAnyValue(vm => vm.TextEditor)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(textEditor, x => x.Text)
                .DisposeWith(_disposable);
            textEditor.ShortcutAction = () =>
            {
                if (ViewModel.OpenedPage != null)
                {
                    Log.Logger.Debug("Request editor");
                    ViewModel.EditingState = EditingState.Update;
                    ViewModel.EditingTitle = titleField.Text;
                    ViewModel.EditingText = textEditor.Text;
                    this.Controller.RequestHome();
                    this.Controller.RequestEditor();
                    this.RequestStop();
                }
            };
            editorFrame.Add(textEditor);

            var scrollBar = new ScrollBarView(textEditor, true);
            scrollBar.ChangedPosition += () =>
            {
                textEditor.TopRow = scrollBar.Position;
                if (textEditor.TopRow != scrollBar.Position)
                {
                    scrollBar.Position = textEditor.TopRow;
                }
                textEditor.SetNeedsDisplay();
            };
            scrollBar.OtherScrollBarView.ChangedPosition += () =>
            {
                textEditor.LeftColumn = scrollBar.OtherScrollBarView.Position;
                if (textEditor.LeftColumn != scrollBar.OtherScrollBarView.Position)
                {
                    scrollBar.OtherScrollBarView.Position = textEditor.LeftColumn;
                }
                textEditor.SetNeedsDisplay();
            };
            scrollBar.VisibleChanged += () =>
            {
                if (scrollBar.Visible && textEditor.RightOffset == 0)
                {
                    textEditor.RightOffset = 1;
                }
                else if (!scrollBar.Visible && textEditor.RightOffset == 1)
                {
                    textEditor.RightOffset = 0;
                }
            };
            scrollBar.OtherScrollBarView.VisibleChanged += () =>
            {
                if (scrollBar.OtherScrollBarView.Visible && textEditor.BottomOffset == 0)
                {
                    textEditor.BottomOffset = 1;
                }
                else if (!scrollBar.OtherScrollBarView.Visible && textEditor.BottomOffset == 1)
                {
                    textEditor.BottomOffset = 0;
                }
            };
            textEditor.DrawContent += (e) =>
            {
                scrollBar.Size = textEditor.Lines;
                scrollBar.Position = textEditor.TopRow;
                if (scrollBar.OtherScrollBarView != null)
                {
                    scrollBar.OtherScrollBarView.Size = textEditor.Maxlength;
                    scrollBar.OtherScrollBarView.Position = textEditor.LeftColumn;
                }
                scrollBar.LayoutSubviews();
                scrollBar.Refresh();
            };

            return editorFrame;
        }

        MenuItem[] CreateColorSchemeMenuItems()
        {
            List<MenuItem> menuItems = new List<MenuItem>();
            foreach (var sc in Colors.ColorSchemes)
            {
                var item = new MenuItem();
                item.Title = $"_{sc.Key}";
                item.Shortcut = Key.AltMask | (Key)sc.Key.Substring(0, 1)[0];
                item.CheckType |= MenuItemCheckStyle.Radio;
                item.Checked = sc.Value == _colorScheme;
                item.Action += () =>
                {
                    ColorScheme = _colorScheme = sc.Value;
                    SetNeedsDisplay();
                    foreach (var menuItem in menuItems)
                    {
                        menuItem.Checked = menuItem.Title.Equals($"_{sc.Key}") && sc.Value == _colorScheme;
                    }
                };
                menuItems.Add(item);
            }
            return menuItems.ToArray();
        }

        public ScreenController Controller { get; set; }
        public MemoriaNoteViewModel ViewModel { get; set; }

        protected override void Dispose(bool disposing)
        {
            _disposable.Dispose();
            base.Dispose(disposing);
        }

        object IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = (MemoriaNoteViewModel)value;
        }
    }
}