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
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>Processes the queued screens until completion or cancellation.</summary>
        /// <param name="vm">The presentation state shared by the screens.</param>
        /// <param name="cancellationToken">Cancels screen processing.</param>
        /// <returns>The screen processing exit code.</returns>
        public async Task<int> StartAsync(
            MemoriaNoteViewModel vm,
            CancellationToken cancellationToken)
        {
            while(views.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await OnScreenProcedureAsync(
                    views.Pop(),
                    this,
                    vm,
                    cancellationToken);
            }

            return 0;              
        }

        static async Task OnScreenProcedureAsync(
            Type type,
            ScreenController sc,
            MemoriaNoteViewModel vm,
            CancellationToken cancellationToken)
        {
            if (type.Equals(typeof(HomeView))) 
            {        
                HomeView.Run(
                    sc,
                    vm,
                    sc._loggerFactory.CreateLogger<HomeView>(),
                    cancellationToken);
            }
            else if (type.Equals(typeof(ManageView))) 
            {        
                ManageView.Run(
                    sc,
                    vm,
                    sc._loggerFactory.CreateLogger<ManageView>(),
                    cancellationToken);
            }
            else if (type.Equals(typeof(EditorView)))
            {
                await EditorView.RunAsync(
                    sc,
                    vm,
                    sc._terminalEditorFactory,
                    sc._loggerFactory.CreateLogger<EditorView>(),
                    cancellationToken);
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
