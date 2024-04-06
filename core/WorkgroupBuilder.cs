using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a builder class for creating instances of Workgroup objects. 
    /// Inherits from ReactiveObject and implements the IWorkgroup interface.
    /// </summary>
    [DataContract]
    public class WorkgroupBuilder : ReactiveObject, IWorkgroup
    {
        /// <summary>
        /// Generate a new instance of WorkgroupBuilder with the specified parameters.
        /// </summary>
        /// <param name="name">The name of the workgroup.</param>
        /// <param name="useDataSources">The list of data sources to be used.</param>
        /// <param name="selectedNoteName">The name of the selected note (optional).</param>
        /// <returns>A new instance of WorkgroupBuilder initialized with the provided parameters.</returns>
        public static WorkgroupBuilder Generate(string name, List<string> useDataSources, string selectedNoteName = null)
        {
            var builder = new WorkgroupBuilder();
            builder.Name = name;
            builder.SelectedNoteName = selectedNoteName;
            builder.UseDataSources.AddRange(useDataSources);
            return builder;
        }

        public WorkgroupBuilder() { }

        /// <summary>
        /// Build and return a new instance of Workgroup based on the current state of the WorkgroupBuilder.
        /// </summary>
        /// <returns>A new instance of Workgroup with the specified name, notes, and selected note.</returns>
        public virtual Workgroup Build()
        {
            var wg = new Workgroup();
            wg.Name = this.Name;
            wg.Notes.AddRange(GetNoteItems(this.UseDataSources));
            if (this.SelectedNoteName != null)
            {
                wg.SelectedNote = wg.Notes.FirstOrDefault(n => SelectedNoteName == n.Metadata.Name);
                if (wg.SelectedNote == null)
                    wg.SelectedNote = wg.Notes.FirstOrDefault();
            }
            else
                wg.SelectedNote = wg.Notes.FirstOrDefault();

            return wg;
        }

        /// <summary>
        /// Method to create a list of Note objects based on the provided data sources.
        /// </summary>
        /// <param name="dataSources">The list of data sources used to create the Note objects.</param>
        /// <returns>A list of Note objects generated from the data sources.</returns>
        List<Note> GetNoteItems(IEnumerable<string> dataSources) => dataSources.Select(source => new Note(source)).ToList();

        /// <summary>
        /// Name of the workgroup.
        /// </summary>
        [DataMember][Reactive] public string Name { get; set; }

        /// <summary>
        /// Name of the selected note in the workgroup.
        /// </summary>
        [DataMember][Reactive] public string SelectedNoteName { get; set; }

        /// <summary>
        /// List of data sources used in the workgroup.
        /// </summary>
        [DataMember][Reactive] public List<string> UseDataSources { get; set; } = new List<string>();

        public override string ToString() => Name;

        /// <summary>
        /// Creates a deep copy (clone) of the current instance of WorkgroupBuilder.
        /// </summary>
        /// <returns>A new instance of WorkgroupBuilder that is a deep copy of the original instance.</returns>
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
