using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MemoriaNote
{
    /// <summary>
    /// Represents the serialized settings used to create a Workspace.
    /// </summary>
    [DataContract]
    public class WorkspaceSettings : ReactiveObject
    {
        /// <summary>
        /// Creates default workspace settings with the specified values.
        /// </summary>
        /// <param name="name">The name of the workspace.</param>
        /// <param name="notebookDatabasePaths">The list of data sources to be used.</param>
        /// <param name="selectedNotebookName">The name of the selected note (optional).</param>
        /// <returns>Workspace settings initialized with the provided values.</returns>
        public static WorkspaceSettings CreateDefault(
            string name,
            List<string> notebookDatabasePaths,
            string selectedNotebookName = null)
        {
            var settings = new WorkspaceSettings();
            settings.Name = name;
            settings.SelectedNotebookName = selectedNotebookName;
            settings.NotebookDatabasePaths.AddRange(notebookDatabasePaths);
            return settings;
        }

        /// <summary>
        /// Initializes empty workspace settings.
        /// </summary>
        public WorkspaceSettings() { }

        /// <summary>
        /// Creates a Workspace based on the current settings.
        /// </summary>
        /// <returns>A workspace with the configured name, notebooks, and selection.</returns>
        public virtual Workspace CreateWorkspace()
        {
            var notebooks = CreateNotebooks(NotebookDatabasePaths);
            var selectedNotebook = this.SelectedNotebookName == null
                ? notebooks.FirstOrDefault()
                : notebooks.FirstOrDefault(
                    notebook => SelectedNotebookName == notebook.Metadata.Name) ??
                    notebooks.FirstOrDefault();
            return new Workspace(Name, notebooks, selectedNotebook);
        }

        static List<Note> CreateNotebooks(IEnumerable<string> databasePaths)
        {
            return databasePaths.Select(databasePath => new Note(databasePath)).ToList();
        }

        /// <summary>
        /// Name of the workspace.
        /// </summary>
        [DataMember][Reactive] public string Name { get; set; }

        /// <summary>
        /// Name of the selected note in the workspace.
        /// </summary>
        [DataMember(Name = "SelectedNoteName")]
        [Reactive]
        public string SelectedNotebookName { get; set; }

        /// <summary>
        /// List of data sources used in the workspace.
        /// </summary>
        [DataMember(Name = "UseDataSources")]
        [Reactive]
        public List<string> NotebookDatabasePaths { get; set; } = new List<string>();

        /// <inheritdoc/>
        public override string ToString() => Name;

        /// <summary>
        /// Creates a deep copy (clone) of the current instance of WorkspaceSettings.
        /// </summary>
        /// <returns>A new instance of WorkspaceSettings that is a deep copy of the original instance.</returns>
        public WorkspaceSettings Clone()
        {
            return new WorkspaceSettings()
            {
                Name = this.Name,
                SelectedNotebookName = this.SelectedNotebookName,
                NotebookDatabasePaths = new List<string>(NotebookDatabasePaths)
            };
        }
    }
}
