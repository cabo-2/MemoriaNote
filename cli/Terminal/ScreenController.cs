using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Cli.Editors;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli.Terminal
{
    /// <summary>Coordinates terminal screens using the existing stack order.</summary>
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

        protected readonly Stack<Type> _views;
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
            _views = new Stack<Type>();
        }

        /// <summary>Processes the queued screens until completion or cancellation.</summary>
        /// <param name="vm">The presentation state shared by the screens.</param>
        /// <param name="cancellationToken">Cancels screen processing.</param>
        /// <returns>The screen processing exit code.</returns>
        public async Task<int> StartAsync(
            MemoriaNoteViewModel vm,
            CancellationToken cancellationToken)
        {
            while (_views.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await OnScreenProcedureAsync(
                    _views.Pop(),
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

        /// <summary>Queues the home screen.</summary>
        public void RequestHome()
        {
            _views.Push(typeof(HomeView));
        }

        /// <summary>Queues the management screen.</summary>
        public void RequestManage()
        {
            _views.Push(typeof(ManageView));
        }

        /// <summary>Queues the external editor workflow.</summary>
        public void RequestEditor()
        {
            _views.Push(typeof(EditorView));
        }

        /// <summary>Removes every queued screen.</summary>
        public void RequestExit()
        {
            _views.Clear();
        }
    }
}
