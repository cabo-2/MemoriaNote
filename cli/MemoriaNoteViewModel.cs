using System;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using NStack;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;

namespace MemoriaNote.Cli
{

    /// <summary>
    /// This class represents the ViewModel for MemoriaNote application, inheriting from MemoriaNoteService
    /// </summary>
    [DataContract]
    public class MemoriaNoteViewModel : MemoriaNoteService
    {
        public MemoriaNoteViewModel() : base()
        {
        }
    }
}