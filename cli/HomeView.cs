using System;
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
                            "About", () =>  MessageBox.Query ("About", ViewHelper.AboutMessage, "_Ok"), null, null, Key.CtrlMask | Key.A)
                    }),
                });
            return MenuBar;
        }

        protected MenuItem[] CreateNoteMenuItems()
        {
            var items = new List<MenuItem>();
            var notes = ViewModel.NoteNames;
            var index = ViewModel.SelectedNoteIndex;
            var maxLen = Math.Min(notes.Count, 10);

            foreach (var i in Enumerable.Range(0, maxLen))
            {
                var isCurrent = (i == index);
                var item = new MenuItem()
                {
                    Title = notes[i],
                    Help = "",
                    Checked = isCurrent,
                    Shortcut = ViewHelper.NumberToKey(i) | Key.CtrlMask | Key.AltMask,
                    Action = () =>
                    {
                        Log.Logger.Debug($"Push Ctrl+Alt+{i.ToString()}");
                        if (ViewModel.SelectedNoteIndex != i)
                        {
                            ConfigurationCli.Instance.Workgroup.SelectedNoteName = ViewModel.NoteNames[i].ToString();
                            ViewModel.Workgroup.SelectedNote = ViewModel.Workgroup.Notes[i];

                            Log.Logger.Debug($"Selected note changed: {ConfigurationCli.Instance.Workgroup.SelectedNoteName}");
                            Controller.RequestHome();
                            Application.RequestStop();
                        }
                    }
                };
                items.Add(item);
            }
            return items.ToArray();
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
                    new StatusItem(Key.F3, "~F3~ " + ViewModel.SearchRangeString, () => {
                        Log.Logger.Debug("Push F3 Function");
                        if (ViewModel.SearchRange == SearchRangeType.Note)
                            ViewModel.SearchRange = ConfigurationCli.Instance.State.SearchRange = SearchRangeType.Workgroup;
                        else
                            ViewModel.SearchRange = ConfigurationCli.Instance.State.SearchRange = SearchRangeType.Note;

                        Controller.RequestHome();
                        Application.RequestStop ();
                        Log.Logger.Debug("Search range changed: " + ViewModel.SearchRangeString);
                    }),
                    new StatusItem(Key.F4, "~F4~ " + ViewModel.SearchMethodString, () => {
                        Log.Logger.Debug("Push F4 Function");
                        if (ViewModel.SearchMethod == SearchMethodType.Headline)
                            ViewModel.SearchMethod = ConfigurationCli.Instance.State.SearchMethod = SearchMethodType.FullText;
                        else
                            ViewModel.SearchMethod = ConfigurationCli.Instance.State.SearchMethod = SearchMethodType.Headline;

                        Controller.RequestHome();
                        Application.RequestStop ();
                        Log.Logger.Debug("Search method changed: " + ViewModel.SearchMethodString);
                    }),
                    new StatusItem(Key.Null,"                       ",() => {}),
                    new StatusItem(Key.F10, "~F10~ Browse Mode", () => {
                        Log.Logger.Debug("Push F10 Function");

                        ViewModel.SearchRange = SearchRangeType.Note;
                        ViewModel.SearchMethod = SearchMethodType.Headline;

                        Controller.RequestManage();
                        Application.RequestStop ();
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

            var notesView = ViewHelper.CreateNoteName();
            ViewModel
                .WhenAnyValue(
                    vm => vm.NoteNames,
                    vm => vm.SelectedNoteIndex,
                    (list, index) => NStack.ustring.Make(list[index]))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(notesView, x => x.Text)
                .DisposeWith(_disposable);

            var searchTextField = ViewHelper.CreateSearchTextField(notesView, ViewModel.SearchEntry ?? "");
            ViewModel
                .WhenAnyValue(vm => vm.SearchEntry)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(searchTextField, x => x.Text)
                .DisposeWith(_disposable);
            searchTextField.TextChanged += (e) =>
            {
                ViewModel.SearchEntry = searchTextField.Text.ToString();
                Observable.Start(() => { }).InvokeCommand(ViewModel, vm => vm.Search);
            };
            navigation.Add(notesView);
            navigation.Add(searchTextField);

            var notifyField = ViewHelper.CreateNotifyField(searchTextField);
            ViewModel
                .WhenAnyValue(vm => vm.Notification, x => NStack.ustring.Make(x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(notifyField, x => x.Text)
                .DisposeWith(_disposable);
            navigation.Add(notifyField);

            return navigation;
        }

        protected FrameView CreateContentsFrame()
        {
            var contentsFrame = new FrameView("List")
            {
                X = 0,
                Y = ViewHelper.ContentPosX,
                Width = ViewHelper.ContentWidth,
                Height = Dim.Fill(1),
                CanFocus = false,
            };

            var contentsLabel = ViewHelper.CreateContentsLabel();
            ViewModel
                .WhenAnyValue(vm => vm.PlaceHolder, x => NStack.ustring.Make(x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(contentsLabel, x => x.Text)
                .DisposeWith(_disposable);

            var pagePrevButton = ViewHelper.CreatePagePrevButton(contentsLabel);
            pagePrevButton
                .Events()
                .Clicked
                .InvokeCommand(ViewModel, x => x.PagePrev)
                .DisposeWith(_disposable);
            var pageNextButton = ViewHelper.CreatePageNextButton(pagePrevButton);
            pageNextButton
                .Events()
                .Clicked
                .InvokeCommand(ViewModel, x => x.PageNext)
                .DisposeWith(_disposable);
            contentsFrame.Add(contentsLabel);
            contentsFrame.Add(pagePrevButton);
            contentsFrame.Add(pageNextButton);

            var contentsListView = ViewHelper.CreateContentsListView(pageNextButton);
            contentsListView.SelectedItemChanged += (e) =>
            {
                ViewModel.ContentsViewPageIndex = (ViewModel.ContentsViewPageIndex.Item1, e.Item);
                Observable.Start(() => { }).InvokeCommand(ViewModel, vm => vm.OpenText);
            };
            contentsListView.SetSource(ViewModel.ContentViewItems);
            contentsFrame.Add(contentsListView);
            ViewHelper.CreateContentsScrollBar(contentsListView);

            return contentsFrame;
        }

        protected FrameView CreateEditorFrame()
        {
            var editorFrame = new FrameView("Text")
            {
                X = ViewHelper.ContentWidth,
                Y = ViewHelper.ContentPosX,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                CanFocus = true,
            };

            var pageNameField = ViewHelper.CreatePageNameField();
            ViewModel
                .WhenAnyValue(vm => vm.EditingTitle, x => NStack.ustring.Make(x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(pageNameField, x => x.Text)
                .DisposeWith(_disposable);
            var pageUpdateTimeField = ViewHelper.CreatePageUpdateTimeField(pageNameField);
            ViewModel
                .WhenAnyValue(vm => vm.EditingUpdateTime)
                .Select(x => NStack.ustring.Make(" " + x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(pageUpdateTimeField, x => x.Text)
                .DisposeWith(_disposable);
            var emptyField = ViewHelper.CreateEmptyField();
            var noteTitleField = ViewHelper.CreateNoteTitleField(emptyField);
            ViewModel
                .WhenAnyValue(vm => vm.EditingNoteTitle)
                .Select(x => NStack.ustring.Make(" " + x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(noteTitleField, x => x.Text)
                .DisposeWith(_disposable);
            editorFrame.Add(pageNameField, pageUpdateTimeField);
            editorFrame.Add(emptyField, noteTitleField);
            var textEditor = ViewHelper.CreateTextEditor();
            ViewModel
                .WhenAnyValue(vm => vm.EditingText, x => NStack.ustring.Make(x))
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(textEditor, x => x.Text)
                .DisposeWith(_disposable);
            editorFrame.Add(textEditor);
            ViewHelper.CreateTextEditorScrollBar(textEditor);

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