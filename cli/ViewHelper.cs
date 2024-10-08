using System;
using System.Text;
using Terminal.Gui;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Helper class containing methods to create various view elements for the MemoriaNote.Cli application.
    /// </summary>
    public class ViewHelper
    {
        public static int NotesWidth => 42;
        public static int SearchTextWidth => 20;
        public static int NotifyOffsetWidth => 8;
        public static int NotifyWidth => 36;
        public static int ContentPosX => 4;
        public static int ContentWidth => 25;
        public static int PageUpdateTimeWidth => 40;
        public static int NoteTitleWidth => 40;
        public static int EditorPosX => ContentWidth;

        static readonly StringBuilder aboutMessage;
        public static string AboutMessage => aboutMessage.ToString();
        static ViewHelper()
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
            aboutMessage.AppendLine(@"https://github.com/cabo-2/MemoriaNote");
        }

        public static Key NumberToKey(int number)
        {
            switch (number)
            {
                case 0: return Key.D1;
                case 1: return Key.D2;
                case 2: return Key.D3;
                case 3: return Key.D4;
                case 4: return Key.D5;
                case 5: return Key.D6;
                case 6: return Key.D7;
                case 7: return Key.D8;
                case 8: return Key.D9;
                case 9: return Key.D0;
                default: throw new ArgumentException(nameof(number));
            }
        }

        public static Label CreateNoteName() => new Label()
        {
            X = 1,
            Y = 0,
            Width = NotesWidth,
            Height = 1
        };

        public static TextField CreateSearchTextField(View prev, string name) => new TextField(name)
        {
            X = Pos.Right(prev) + 3,
            Y = 0,
            Width = SearchTextWidth,
            Height = 1,
            CanFocus = true,
        };

        public static Label CreateNotifyField(View prev) => new Label()
        {
            X = Pos.Right(prev) + NotifyOffsetWidth,
            Y = 0,
            Width = NotifyWidth,
            Height = 1,
            CanFocus = false,
            TextAlignment = TextAlignment.Right
        };

        public static Label CreateContentsLabel() => new Label()
        {
            X = 0,
            Y = 0,
            Width = ContentWidth,
            Height = 1,
            TextAlignment = TextAlignment.Centered
        };

        public static Button CreatePagePrevButton(View prev) => new Button(" <<< ")
        {
            X = 0,
            Y = 1,
            Width = 7,
            Height = 1,
            CanFocus = false
        };

        public static Button CreatePageNextButton(View prev) => new Button(" >>> ")
        {
            X = ContentWidth - 7 - 4,
            Y = 1,
            Width = 7,
            Height = 1,
            CanFocus = false
        };

        public static ListView CreateContentsListView(View prev) => new ListView()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(0),
            Height = Dim.Fill(0),
            AllowsMarking = false,
            CanFocus = true,
        };

        public static ScrollBarView CreateContentsScrollBar(ListView contentsListView)
        {
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
            return scrollBar;
        }

        public static TextField CreatePageNameField() => new TextField()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill() - PageUpdateTimeWidth,
            Height = 1,
            CanFocus = false,
            ReadOnly = true
        };

        public static TextField CreateEmptyField() => new TextField()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill() - NoteTitleWidth,
            Height = 1,
            CanFocus = false,
            ReadOnly = true
        };

        public static TextField CreatePageUpdateTimeField(View prev) => new TextField()
        {
            X = Pos.Right(prev),
            Y = 0,
            Width = PageUpdateTimeWidth,
            Height = 1,
            CanFocus = false,
            ReadOnly = true,
            TextAlignment = TextAlignment.Right
        };

        public static TextField CreateNoteTitleField(View prev) => new TextField()
        {
            X = Pos.Right(prev),
            Y = 1,
            Width = NoteTitleWidth,
            Height = 1,
            CanFocus = false,
            ReadOnly = true,
            TextAlignment = TextAlignment.Right
        };

        public static TextView CreateTextEditor() => new TextView()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BottomOffset = 1,
            RightOffset = 1,
            CanFocus = true,
            ReadOnly = true,
            WordWrap = true
        };

        public static ScrollBarView CreateTextEditorScrollBar(TextView textEditor)
        {
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
            return scrollBar;
        }
    }
}