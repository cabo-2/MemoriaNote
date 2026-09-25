using System;
using MemoriaNote.Domain;

namespace MemoriaNote
{
    /// <summary>Represents the versioned selection state stored in a workspace.</summary>
    public sealed class WorkspaceConfiguration
    {
        /// <summary>Gets the workspace configuration format supported by this version.</summary>
        public const long CurrentFormatVersion = 1;

        WorkspaceConfiguration(NotebookFileName currentNotebook)
        {
            CurrentNotebook = currentNotebook;
        }

        /// <summary>Gets the persisted workspace configuration format version.</summary>
        public long FormatVersion => CurrentFormatVersion;

        /// <summary>Gets the selected notebook leaf name, or null at the workspace root.</summary>
        public NotebookFileName CurrentNotebook { get; }

        /// <summary>Creates configuration representing the workspace root.</summary>
        /// <returns>A configuration without a selected notebook.</returns>
        public static WorkspaceConfiguration CreateRoot()
        {
            return new WorkspaceConfiguration(null);
        }

        /// <summary>Creates configuration selecting the specified notebook.</summary>
        /// <param name="currentNotebook">The normalized notebook leaf name.</param>
        /// <returns>A configuration with the specified current notebook.</returns>
        public static WorkspaceConfiguration CreateSelected(NotebookFileName currentNotebook)
        {
            return new WorkspaceConfiguration(
                currentNotebook ?? throw new ArgumentNullException(nameof(currentNotebook)));
        }
    }
}
