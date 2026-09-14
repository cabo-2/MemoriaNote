using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using DynamicData;
using DynamicData.Binding;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Reactive.Concurrency;
using ReactiveUI;
using Terminal.Gui;
using McMaster.Extensions.CommandLineUtils;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli
{
    public class ScreenController
    {
        //
        // Implements screen control by stack(LIFO)
        // 
        // Screen transition patterns
        //  Home -> Exit
        //  Home -> Edit -> Home -> Exit
        //  Edit -> Home -> Exit
        //

        protected Stack<Type> views;
        readonly TerminalEditorFactory _terminalEditorFactory;
        readonly ILoggerFactory _loggerFactory;

        /// <summary>Initializes a screen controller with its external adapters.</summary>
        /// <param name="terminalEditorFactory">The terminal editor factory.</param>
        /// <param name="loggerFactory">The shared logger factory.</param>
        public ScreenController(
            TerminalEditorFactory terminalEditorFactory,
            ILoggerFactory loggerFactory)
        {
            _terminalEditorFactory = terminalEditorFactory ??
                throw new ArgumentNullException(nameof(terminalEditorFactory));
            _loggerFactory = loggerFactory ??
                throw new ArgumentNullException(nameof(loggerFactory));
            views = new Stack<Type>();
        }

        public int Start(MemoriaNoteViewModel vm)
        {
            while(views.Count > 0)
                OnScreenProcedure(views.Pop(), this, vm);

            return 0;              
        }

        static void OnScreenProcedure(Type type, ScreenController sc, MemoriaNoteViewModel vm)
        {
            if (type.Equals(typeof(HomeView))) 
            {        
                HomeView.Run(sc, vm, sc._loggerFactory.CreateLogger<HomeView>());
            }
            else if (type.Equals(typeof(ManageView))) 
            {        
                ManageView.Run(sc, vm, sc._loggerFactory.CreateLogger<ManageView>());
            }
            else if (type.Equals(typeof(EditorView)))
            {
                EditorView.Run(
                    sc,
                    vm,
                    sc._terminalEditorFactory,
                    sc._loggerFactory.CreateLogger<EditorView>());
            }
            else
                throw new NotImplementedException(nameof(type));
        }

        public void RequestHome()
        {
            views.Push(typeof(HomeView));   
        }     

        public void RequestManage()
        {
            views.Push(typeof(ManageView));   
        }  

        public void RequestEditor()
        {         
            views.Push(typeof(EditorView));           
        }     

        public void RequestExit()
        {
            views.Clear();
        }    

        //public ReadOnlyCollection<ITerminalScreen> Controls 
        //                  => new ReadOnlyCollection<ITerminalScreen>(_views.ToList());       
    }
}
