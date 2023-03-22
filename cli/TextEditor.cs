using System;
using System.Linq;
using System.Reflection;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Collections.Generic;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;
using NStack;

namespace MemoriaNote.Cli
{
    public class TextEditor : TextView, ITextEditor
    {
    }

    public interface ITextEditor
    {
        ustring Text { get; set; }
    }
}