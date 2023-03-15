using System;
using System.Text;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Collections.Generic;
using NStack;
using ReactiveUI;
using Terminal.Gui;
using ReactiveMarbles.ObservableEvents;

namespace MemoriaNote.Cli {

	public class MemoriaNoteWindow : Toplevel
	{
		protected FrameView _navigation;
		protected Label _bookGroupLabel;
		protected ComboBox _bookGroupBox;
		protected Label _contentsLabel;
		protected ComboBox _contentsBox;

		protected Label _searchTextLabel;
		protected TextField _searchTextField;
		protected Label _searchRangeLabel;
		protected ComboBox _searchRangeBox;

        protected Label _notificationLabel;
        protected TextField _notificationField;

		protected FrameView _pageFrame;
		protected Button _pagePrevButton;
		protected Label _pageLabel;
		protected Button _pageNextButton;
		protected ListView _pageListView;
		protected ScrollBarView _scrollBar;

		protected FrameView _editorFrame;
		protected TextField _titleTextField;
		protected TextView _textEditorView;

		const int BookGroupWidth = 20;
		const int ContentsWidth = 25;
		const int SearchTextWidth = 20;
		const int SearchRangeWidth = 15;
        const int NotificationWidth = 30;
		const int PagePosX = 5;
		const int PageWidth = 25;


		List<ustring> _categories = new List<ustring>() {"test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test", "test2", "test" };

		StringBuilder _aboutMessage;

		static bool _isFirstRunning = true;
		ColorScheme _colorScheme;

		public MemoriaNoteWindow ()
		{
			_aboutMessage = new StringBuilder();
			_aboutMessage.AppendLine (@"");
			_aboutMessage.AppendLine (@" __  __                           _       ");
			_aboutMessage.AppendLine (@"|  \/  | ___ _ __ ___   ___  _ __(_) __ _ ");
			_aboutMessage.AppendLine (@"| |\/| |/ _ \ '_ ` _ \ / _ \| '__| |/ _` |");
			_aboutMessage.AppendLine (@"| |  | |  __/ | | | | | (_) | |  | | (_| |");
			_aboutMessage.AppendLine (@"|_|  |_|\___|_| |_| |_|\___/|_|  |_|\__,_|");
			_aboutMessage.AppendLine (@"  _   _       _           ____ _     ___  ");
			_aboutMessage.AppendLine (@" | \ | | ___ | |_ ___    / ___| |   |_ _| ");
			_aboutMessage.AppendLine (@" |  \| |/ _ \| __/ _ \  | |   | |    | |  ");
			_aboutMessage.AppendLine (@" | |\  | (_) | ||  __/  | |___| |___ | |  ");
			_aboutMessage.AppendLine (@" |_| \_|\___/ \__\___|   \____|_____|___| ");
			_aboutMessage.AppendLine (@"");
			_aboutMessage.AppendLine (@"https://github.com/gui-cs/Terminal.Gui");

			InitializeComponent();
		}

        private void InitializeComponent ()
        {
            ColorScheme = _colorScheme = Colors.Base;
            MenuBar = new MenuBar(new MenuBarItem[] {
                    new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem ("_Quit", "Exit", () => RequestStop(), null, null, Key.Q | Key.CtrlMask)
                    }),
                    new MenuBarItem ("_Theme", CreateColorSchemeMenuItems()),
                    new MenuBarItem ("_Help", new MenuItem [] {
                        new MenuItem ("_About...",
                            "About", () =>  MessageBox.Query ("About", _aboutMessage.ToString(), "_Ok"), null, null, Key.CtrlMask | Key.A),
                    }),
                });

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
                        _pageFrame.Height = Dim.Fill(StatusBar.Visible ? 1 : 0);
                        _editorFrame.Height = Dim.Fill(StatusBar.Visible ? 1 : 0);
                        LayoutSubviews();
                        SetChildNeedsDisplay();
                    })
                };

            _navigation = new FrameView("Navigation")
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = 4,
                CanFocus = false
            };
            _bookGroupLabel = new Label("Notes group:")
            {
                X = 1,
                Y = 0,
                Width = BookGroupWidth,
                Height = 1
            };
            _bookGroupBox = new ComboBox("")
            {
                X = Pos.Left(_bookGroupLabel),
                Y = 1,
                Width = BookGroupWidth,
                Height = 1,
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.B
            };
            _bookGroupBox.ShortcutAction = () => _bookGroupBox.SetFocus();
            _navigation.Add(_bookGroupLabel, _bookGroupBox);

            _contentsLabel = new Label("Note:")
            {
                X = Pos.Right(_bookGroupLabel) + 2,
                Y = 0,
                Width = ContentsWidth,
                Height = 1
            };
            _contentsBox = new ComboBox("")
            {
                X = Pos.Left(_contentsLabel),
                Y = 1,
                Width = ContentsWidth,
                Height = 1,
                CanFocus = true
            };

            _searchTextLabel = new Label("Search:")
            {
                X = Pos.Right(_contentsLabel) + 2,
                Y = 0,
                Width = SearchTextWidth,
                Height = 1
            };
            _searchTextField = new TextField("")
            {
                X = Pos.Left(_searchTextLabel),
                Y = 1,
                Width = SearchTextWidth,
                Height = 1,
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.S
            };
            _searchTextField.TextChanged += (text) => { };
            _navigation.Add(_contentsLabel);
            _navigation.Add(_contentsBox);

            _searchRangeLabel = new Label("Search Range:")
            {
                X = Pos.Right(_searchTextLabel) + 2,
                Y = 0,
                Width = SearchRangeWidth,
                Height = 1
            };
            _searchRangeBox = new ComboBox("")
            {
                X = Pos.Left(_searchRangeLabel),
                Y = 1,
                Width = SearchRangeWidth,
                Height = 1,
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.R
            };
            _navigation.Add(_searchTextLabel, _searchTextField);
            _navigation.Add(_searchRangeLabel, _searchRangeBox);

            _notificationLabel = new Label("Notification:")
            {
                X = Pos.Right(_searchRangeLabel) + 3,
                Y = 0,
                Width = NotificationWidth,
                Height = 1
            };
            _notificationField = new TextField("Warning!!!") 
            {
                X = Pos.Left(_notificationLabel),
                Y = 1,
                Width = NotificationWidth,
                Height = 1,
                CanFocus = false                                
            };
            _navigation.Add(_notificationLabel, _notificationField);

            _pageFrame = new FrameView("Pages")
            {
                X = 0,
                Y = PagePosX,
                Width = PageWidth,
                Height = Dim.Fill(1),
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.P
            };
            _pageFrame.ShortcutAction = () => _pageFrame.SetFocus();

            _pageLabel = new Label("10000 of 10000")
            {
                X = 0,
                Y = 0,
                Width = PageWidth,
                Height = 1,
                TextAlignment = TextAlignment.Centered
            };
            _pagePrevButton = new Button(" <<< ")
            {
                X = 0,
                Y = 1,
                Width = 7,
                Height = 1,
                CanFocus = false
            };
            _pageNextButton = new Button(" >>> ")
            {
                X = PageWidth - 7 - 4,
                Y = 1,
                Width = 7,
                Height = 1,
                CanFocus = false
            };
            _pageFrame.Add(_pageLabel);
            _pageFrame.Add(_pagePrevButton);
            _pageFrame.Add(_pageNextButton);

            _pageListView = new ListView(_categories)
            {
                X = 0,
                Y = 2,
                Width = Dim.Fill(0),
                Height = Dim.Fill(0),
                AllowsMarking = false,
                CanFocus = true,
            };
            _pageListView.OpenSelectedItem += (a) =>
            {
                _editorFrame.SetFocus();
            };
            _pageListView.SelectedItemChanged += CategoryListView_SelectedChanged;
            _pageFrame.Add(_pageListView);

            _scrollBar = new ScrollBarView(_pageListView, true);

            _scrollBar.ChangedPosition += () =>
            {
                _pageListView.TopItem = _scrollBar.Position;
                if (_pageListView.TopItem != _scrollBar.Position)
                {
                    _scrollBar.Position = _pageListView.TopItem;
                }
                _pageListView.SetNeedsDisplay();
            };

            _scrollBar.OtherScrollBarView.ChangedPosition += () =>
            {
                _pageListView.LeftItem = _scrollBar.OtherScrollBarView.Position;
                if (_pageListView.LeftItem != _scrollBar.OtherScrollBarView.Position)
                {
                    _scrollBar.OtherScrollBarView.Position = _pageListView.LeftItem;
                }
                _pageListView.SetNeedsDisplay();
            };

            _pageListView.DrawContent += (e) =>
            {
                _scrollBar.Size = _pageListView.Source.Count - 1;
                _scrollBar.Position = _pageListView.TopItem;
                _scrollBar.OtherScrollBarView.Size = _pageListView.Maxlength - 1;
                _scrollBar.OtherScrollBarView.Position = _pageListView.LeftItem;
                _scrollBar.Refresh();
            };

            _editorFrame = new FrameView("Editor")
            {
                X = 25,
                Y = 5,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.S
            };
            _editorFrame.ShortcutAction = () => _editorFrame.SetFocus();

            _titleTextField = new TextField("")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                CanFocus = true,
                Shortcut = Key.CtrlMask | Key.T
            };
            _titleTextField.ShortcutAction = () => _titleTextField.SetFocus();
            _textEditorView = new TextView()
            {
                X = 0,
                Y = Pos.Bottom(_titleTextField) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                BottomOffset = 1,
                RightOffset = 1,
                CanFocus = false
                //Shortcut = Key.CtrlMask | Key.E
            };
            //_textEditorView.ShortcutAction = () => _textEditorView.SetFocus();
			_editorFrame.Add(_titleTextField, _textEditorView);

            KeyDown += KeyDownHandler;

            Add(MenuBar);
            Add(_navigation);
            Add(_pageFrame);
            Add(_editorFrame);
            Add(StatusBar);

            Loaded += OnWindowLoaded;

            // Restore previous selections
            _pageListView.SelectedItem = 0;
        }

        void OnWindowLoaded ()
		{
			if (_colorScheme == null) {
				ColorScheme = _colorScheme = Colors.Base;
			}
			Loaded -= OnWindowLoaded;
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

		void KeyDownHandler (View.KeyEventEventArgs a)
		{
			/*
			if (a.KeyEvent.IsCapslock) {
				Capslock.Title = "Caps: On";
				StatusBar.SetNeedsDisplay ();
			} else {
				Capslock.Title = "Caps: Off";
				StatusBar.SetNeedsDisplay ();
			}

			if (a.KeyEvent.IsNumlock) {
				Numlock.Title = "Num: On";
				StatusBar.SetNeedsDisplay ();
			} else {
				Numlock.Title = "Num: Off";
				StatusBar.SetNeedsDisplay ();
			}

			if (a.KeyEvent.IsScrolllock) {
				Scrolllock.Title = "Scroll: On";
				StatusBar.SetNeedsDisplay ();
			} else {
				Scrolllock.Title = "Scroll: Off";
				StatusBar.SetNeedsDisplay ();
			}*/
		}

        void TextEditorView_KeyDown(View.KeyEventEventArgs e) {
            
        }
        void TextEditorView_MouseDown(MouseEventArgs e) {
            
        }

		void CategoryListView_SelectedChanged (ListViewItemEventArgs e)
		{
			// var item = _categories [e.Item];
			// List<string> newlist;
			// if (e.Item == 0) {
			// 	// First category is "All"
			// 	newlist = _scenarios;

			// } else {
			// 	newlist = _scenarios.Where (s => s.GetCategories ().Contains (item)).ToList ();
			// }
			// ScenarioListView.SetSource (newlist.ToList ());
		}
	}

}