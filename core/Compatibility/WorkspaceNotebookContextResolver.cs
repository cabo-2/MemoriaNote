using System;
using System.Collections.Generic;
using System.Linq;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Compatibility
{
    /// <summary>
    /// Resolves application notebook contexts from the current workspace collection.
    /// </summary>
    internal sealed class WorkspaceNotebookContextResolver : INotebookContextResolver
    {
        readonly Func<IEnumerable<Notebook>> _notebooks;

        internal WorkspaceNotebookContextResolver(Func<IEnumerable<Notebook>> notebooks)
        {
            _notebooks = notebooks ?? throw new ArgumentNullException(nameof(notebooks));
        }

        /// <inheritdoc/>
        public NotebookContext Resolve(NotebookId notebookId)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));

            var notebook = ResolveNotebook(notebookId);
            return notebook == null
                ? null
                : new NotebookContext(
                    notebookId,
                    notebook.Metadata?.ReadOnly == true,
                    notebook.Repository,
                    notebook.SearchRepository);
        }

        internal Notebook ResolveNotebook(NotebookId notebookId)
        {
            return _notebooks().FirstOrDefault(notebook =>
                NotebookId.FromDatabasePath(notebook.DatabasePath) == notebookId);
        }
    }
}
