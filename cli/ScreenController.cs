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

        public ScreenController() 
        {
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
                HomeView.Run(sc, vm);
            }
            else if (type.Equals(typeof(EditView))) 
            {        
                EditView.Run(sc, vm);
            }
            else if (type.Equals(typeof(EditorView)))
            {
                EditorView.Run(sc, vm);               
            }
            else
                throw new NotImplementedException(nameof(type));
        }

        public void RequestHome()
        {
            views.Push(typeof(HomeView));   
        }     

        public void RequestEdit()
        {
            views.Push(typeof(EditView));   
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