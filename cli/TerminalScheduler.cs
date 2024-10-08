﻿// MIT License
//
// Copyright 2007-2011 Novell Inc
// Copyright 2017 Microsoft Corp
//
// The following code is adapted from Terminal.Gui.
// Original source code from: https://github.com/gui-cs/Terminal.Gui/blob/develop/ReactiveExample/TerminalScheduler.cs

using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Terminal.Gui;

namespace MemoriaNote.Cli
{
    public class TerminalScheduler : LocalScheduler
    {
        public static readonly TerminalScheduler Default = new TerminalScheduler();
        TerminalScheduler() { }

        public override IDisposable Schedule<TState>(
            TState state, TimeSpan dueTime,
            Func<IScheduler, TState, IDisposable> action)
        {

            IDisposable PostOnMainLoop()
            {
                var composite = new CompositeDisposable(2);
                var cancellation = new CancellationDisposable();
                Application.MainLoop.Invoke(() =>
                {
                    if (!cancellation.Token.IsCancellationRequested)
                        composite.Add(action(this, state));
                });
                composite.Add(cancellation);
                return composite;
            }

            IDisposable PostOnMainLoopAsTimeout()
            {
                var composite = new CompositeDisposable(2);
                var timeout = Application.MainLoop.AddTimeout(dueTime, args =>
                {
                    composite.Add(action(this, state));
                    return false;
                });
                composite.Add(Disposable.Create(() => Application.MainLoop.RemoveTimeout(timeout)));
                return composite;
            }

            return dueTime == TimeSpan.Zero
                ? PostOnMainLoop()
                : PostOnMainLoopAsTimeout();
        }
    }
}