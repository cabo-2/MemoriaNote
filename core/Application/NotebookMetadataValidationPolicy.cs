using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Identifies a notebook metadata validation failure without presentation wording.
    /// </summary>
    public enum NotebookMetadataErrorCode
    {
        /// <summary>The notebook name is empty or whitespace.</summary>
        NameRequired,

        /// <summary>The requested notebook name is already used in the workspace.</summary>
        DuplicateName,

        /// <summary>The notebook title is empty or whitespace.</summary>
        TitleRequired
    }

    /// <summary>
    /// Applies notebook metadata rules that do not require persistence I/O.
    /// </summary>
    public sealed class NotebookMetadataValidationPolicy
    {
        /// <summary>Validates a proposed notebook metadata update.</summary>
        /// <param name="notebookId">The notebook being updated.</param>
        /// <param name="update">The proposed metadata state.</param>
        /// <param name="workspace">The workspace used for duplicate-name validation.</param>
        /// <returns>The first validation error in legacy validation order, or an empty list.</returns>
        public IReadOnlyList<NotebookMetadataErrorCode> Validate(
            NotebookId notebookId,
            NotebookMetadataUpdate update,
            Workspace workspace)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (update == null)
                throw new ArgumentNullException(nameof(update));
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (string.IsNullOrWhiteSpace(update.Name))
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.NameRequired
                });
            }

            var duplicateName = workspace.Notebooks
                .Where(notebook =>
                    NotebookId.FromDatabasePath(notebook.DatabasePath) != notebookId)
                .Select(notebook => notebook.Metadata?.Name)
                .Any(name => string.Equals(name, update.Name, StringComparison.Ordinal));
            if (duplicateName)
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.DuplicateName
                });
            }

            if (string.IsNullOrWhiteSpace(update.Title))
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.TitleRequired
                });
            }

            return Array.Empty<NotebookMetadataErrorCode>();
        }
    }
}
