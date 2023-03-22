using System;
using System.Text;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Linq;

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using NStack;
using ReactiveUI;
using DynamicData.List;
using ReactiveUI.Fody.Helpers;
using Terminal.Gui;
using ReactiveMarbles.ObservableEvents;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Diagnostics;

namespace MemoriaNote.Cli {

	public class MemoriaNoteView : Toplevel, IViewFor<MemoriaNoteViewModel>, IDisposable
	{
        readonly CompositeDisposable _disposable = new CompositeDisposable();

		protected FrameView _navigation;
		protected FrameView _contentsFrame;
		protected FrameView _editorFrame;
        protected ColorScheme _colorScheme;

		const int NotesLabelWidth = 6;
		const int NotesWidth = 35;
        const int SearchTextLabelWidth = 8;
		const int SearchTextWidth = 20;
		const int SearchRangeLabelWidth = 15;
        const int NotificationLabelWidth = 15;
        const int NotificationWidth = 30;
		const int ContentPosX = 4;
		const int ContentWidth = 25;  
        const int EditorPosX = ContentWidth;     

		static readonly StringBuilder aboutMessage;  
        static MemoriaNoteView()
        {
			aboutMessage = new StringBuilder();
			aboutMessage.AppendLine (@"");
			aboutMessage.AppendLine (@" __  __                           _       ");
			aboutMessage.AppendLine (@"|  \/  | ___ _ __ ___   ___  _ __(_) __ _ ");
			aboutMessage.AppendLine (@"| |\/| |/ _ \ '_ ` _ \ / _ \| '__| |/ _` |");
			aboutMessage.AppendLine (@"| |  | |  __/ | | | | | (_) | |  | | (_| |");
			aboutMessage.AppendLine (@"|_|  |_|\___|_| |_| |_|\___/|_|  |_|\__,_|");
			aboutMessage.AppendLine (@"  _   _       _           ____ _     ___  ");
			aboutMessage.AppendLine (@" | \ | | ___ | |_ ___    / ___| |   |_ _| ");
			aboutMessage.AppendLine (@" |  \| |/ _ \| __/ _ \  | |   | |    | |  ");
			aboutMessage.AppendLine (@" | |\  | (_) | ||  __/  | |___| |___ | |  ");
			aboutMessage.AppendLine (@" |_| \_|\___/ \__\___|   \____|_____|___| ");
			aboutMessage.AppendLine (@"");
			aboutMessage.AppendLine (@"https://github.com/gui-cs/Terminal.Gui");
        }

		public MemoriaNoteView (MemoriaNoteViewModel viewModel)
		{
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
                    new MenuBarItem ("_Theme", CreateColorSchemeMenuItems()),
                    new MenuBarItem ("_Help", new MenuItem [] {
                        new MenuItem ("_About...",
                            "About", () =>  MessageBox.Query ("About", aboutMessage.ToString(), "_Ok"), null, null, Key.CtrlMask | Key.A) 
                    }),
                });
            return MenuBar;
        }

        protected StatusBar CreateStatusBar()
        {
            StatusBar = new StatusBar()
            {
                Visible = true,
            };
            StatusBar.Items = new StatusItem[] {
                    new StatusItem(Key.Q | Key.CtrlMask, "~CTRL-Q~ Quit", () => {
                        Application.RequestStop ();
                    }),
                    new StatusItem(Key.F10, "~F10~ Status Bar", () => {
                        StatusBar.Visible = !StatusBar.Visible;
                        _contentsFrame.Height = Dim.Fill(StatusBar.Visible ? 1 : 0);
                        _editorFrame.Height = Dim.Fill(StatusBar.Visible ? 1 : 0);
                        LayoutSubviews();
                        SetChildNeedsDisplay();
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
                Shortcut = Key.CtrlMask | Key.S
            };
            ViewModel
                .WhenAnyValue(vm => vm.SearchEntry)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(searchTextField, x => x.Text)
                .DisposeWith(_disposable);
            searchTextField.TextChanged += (e) => {                
                ViewModel.SearchEntry = searchTextField.Text.ToString();
                ViewModel.SearchContents();
            };

            navigation.Add(notesLabel);
            navigation.Add(notesView);

            var searchMethodLabel = new Label()
            {
                X = Pos.Right(searchTextField) + 2,
                Y = 0,
                Width = SearchRangeLabelWidth,
                Height = 1
            };
            ViewModel
                .WhenAnyValue(vm => vm.SearchMethodsInfo)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(searchMethodLabel, x => x.Text)
                .DisposeWith(_disposable);        
            navigation.Add(searchTextLabel, searchTextField);
            navigation.Add(searchMethodLabel);

            var notificationLabel = new Label("Notifications:")
            {
                X = Pos.Right(searchMethodLabel) + 3,
                Y = 0,
                Width = NotificationLabelWidth,
                Height = 1
            };            
            var notificationField = new Label() 
            {
                X = Pos.Right(notificationLabel),
                Y = 0,
                Width = NotificationWidth,
                Height = 1,
                CanFocus = false                                
            };
            ViewModel
                .WhenAnyValue(vm => vm.Notification)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(notificationField, x => x.Text)
                .DisposeWith(_disposable);            
            navigation.Add(notificationLabel, notificationField);

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
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.P
            };
            contentsFrame.ShortcutAction = () => contentsFrame.SetFocus();

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
            var pageNextButton = new Button(" >>> ")
            {
                X = ContentWidth - 7 - 4,
                Y = 1,
                Width = 7,
                Height = 1,
                CanFocus = false
            };
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
            contentsListView.SelectedItemChanged += (e) => {
                ViewModel.ContentsViewPageIndex = (ViewModel.ContentsViewPageIndex.Item1, e.Item);
                ViewModel.OpenText();
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
                Shortcut = Key.CtrlMask | Key.S
            };
            editorFrame.ShortcutAction = () => editorFrame.SetFocus();

            var titleField = new TextField()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1
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
                Shortcut = Key.CtrlMask | Key.E
            };
            ViewModel
                .WhenAnyValue(vm => vm.TextEditor)
                .ObserveOn(RxApp.MainThreadScheduler)
                .BindTo(textEditor, x => x.Text)
                .DisposeWith(_disposable);
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
        
		MenuItem [] CreateColorSchemeMenuItems ()
		{
			List<MenuItem> menuItems = new List<MenuItem> ();
			foreach (var sc in Colors.ColorSchemes) {
				var item = new MenuItem ();
				item.Title = $"_{sc.Key}";
				item.Shortcut = Key.AltMask | (Key)sc.Key.Substring (0, 1) [0];
				item.CheckType |= MenuItemCheckStyle.Radio;
				item.Checked = sc.Value == _colorScheme;
				item.Action += () => {
					ColorScheme = _colorScheme = sc.Value;
					SetNeedsDisplay ();
					foreach (var menuItem in menuItems) {
						menuItem.Checked = menuItem.Title.Equals ($"_{sc.Key}") && sc.Value == _colorScheme;
					}
				};
				menuItems.Add (item);
			}
			return menuItems.ToArray ();
		}

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