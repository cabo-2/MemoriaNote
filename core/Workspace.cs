using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a named collection of notebooks and its current selection.
    /// </summary>
    public class Workspace
    {
        readonly IReadOnlyList<Notebook> _notebooks;
        Notebook _selectedNotebook;

        /// <summary>
        /// Initializes an empty workspace.
        /// </summary>
        public Workspace() : this(null, Array.Empty<Notebook>())
        {
        }

        /// <summary>
        /// Initializes a workspace with a defensive copy of its notebooks.
        /// </summary>
        /// <param name="name">The workspace name.</param>
        /// <param name="notebooks">The notebooks contained in the workspace.</param>
        /// <param name="selectedNotebook">The initially selected notebook, or null.</param>
        public Workspace(
            string name,
            IEnumerable<Notebook> notebooks,
            Notebook selectedNotebook = null)
        {
            if (notebooks == null)
                throw new ArgumentNullException(nameof(notebooks));

            var copiedNotebooks = notebooks.ToList();
            if (copiedNotebooks.Any(notebook => notebook == null))
            {
                throw new ArgumentException(
                    "The workspace cannot contain a null note.",
                    nameof(notebooks));
            }

            Name = name;
            _notebooks = new ReadOnlyCollection<Notebook>(copiedNotebooks);
            SelectNotebook(selectedNotebook);
        }

        /// <summary>
        /// Gets the notebooks stored in this workspace.
        /// </summary>
        public IReadOnlyList<Notebook> Notebooks => _notebooks;

        /// <summary>
        /// Gets the notebook database paths in workspace order.
        /// </summary>
        public List<string> NotebookDatabasePaths => _notebooks
            .Select(notebook => notebook.DatabasePath)
            .ToList();

        /// <summary>
        /// Gets the currently selected notebook.
        /// </summary>
        public Notebook SelectedNotebook => _selectedNotebook;

        /// <summary>
        /// Selects a notebook contained in this workspace, or clears the selection.
        /// </summary>
        /// <param name="notebook">The notebook to select, or null to clear the selection.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="notebook"/> does not belong to this workspace.
        /// </exception>
        public void SelectNotebook(Notebook notebook)
        {
            if (notebook == null)
            {
                _selectedNotebook = null;
                return;
            }

            var notebookId = NotebookId.FromDatabasePath(notebook.DatabasePath);
            var ownedNotebook = _notebooks.FirstOrDefault(candidate =>
                NotebookId.FromDatabasePath(candidate.DatabasePath) == notebookId);
            if (ownedNotebook == null)
            {
                throw new ArgumentException(
                    "The selected note must belong to the workspace.",
                    nameof(notebook));
            }

            _selectedNotebook = ownedNotebook;
        }

        /// <summary>
        /// Gets the name of the currently selected notebook.
        /// </summary>
        public string SelectedNotebookName => SelectedNotebook?.ToString();

        /// <summary>
        /// Gets or sets the workspace name.
        /// </summary>
        public string Name { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }
}
