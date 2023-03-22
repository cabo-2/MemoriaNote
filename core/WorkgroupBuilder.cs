using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MemoriaNote
{
    [DataContract]
    public class WorkgroupBuilder : ReactiveObject, IWorkgroup
    {
        public static WorkgroupBuilder Generate(string name, List<string> useDataSources, string selectedNoteName = null)
        {
            var builder = new WorkgroupBuilder();
            builder.Name = name;
            builder.SelectedNoteName = selectedNoteName;
            builder.UseDataSources.AddRange(useDataSources);
            return builder;
        }   

        public WorkgroupBuilder() {}

        public virtual Workgroup Build() 
        {
            var wg = new Workgroup();
            wg.Name = this.Name;
            wg.Notes.AddRange(GetNoteItems(this.UseDataSources));
            if (this.SelectedNoteName != null)
            {
                wg.SelectedNote = wg.Notes.FirstOrDefault( n => SelectedNoteName == n.ToString() );
                if (wg.SelectedNote == null)
                    wg.SelectedNote = wg.Notes.FirstOrDefault();
            }
            else
                wg.SelectedNote = wg.Notes.FirstOrDefault();

            return wg;
        }

        List<Note> GetNoteItems(IEnumerable<string> dataSources) => dataSources.Select( source => new Note(source) ).ToList();

        [DataMember][Reactive] public string Name { get; set; }
        [DataMember][Reactive] public string SelectedNoteName { get; set; }
        [DataMember][Reactive] public List<string> UseDataSources { get; set; } = new List<string>();

        public override string ToString() => Name;

        public WorkgroupBuilder Clone()
        {
            return new WorkgroupBuilder()
            {
                Name = this.Name,
                SelectedNoteName = this.SelectedNoteName,
                UseDataSources = new List<string>(UseDataSources)
            };
        }
    }
}
