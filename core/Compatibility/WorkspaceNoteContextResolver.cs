using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Resolves application note contexts from the current workspace collection.
    /// </summary>
    internal sealed class WorkspaceNoteContextResolver : INoteContextResolver
    {
        readonly Func<IEnumerable<Note>> _notebooks;

        internal WorkspaceNoteContextResolver(Func<IEnumerable<Note>> notebooks)
        {
            _notebooks = notebooks ?? throw new ArgumentNullException(nameof(notebooks));
        }

        /// <inheritdoc/>
        public NoteContext Resolve(NoteId noteId)
        {
            if (noteId == null)
                throw new ArgumentNullException(nameof(noteId));

            var note = ResolveNote(noteId);
            return note == null
                ? null
                : new NoteContext(
                    noteId,
                    note.Metadata?.ReadOnly == true,
                    note.Repository,
                    note.SearchRepository);
        }

        internal Note ResolveNote(NoteId noteId)
        {
            return _notebooks().FirstOrDefault(note =>
                NoteId.FromDataSource(note.DataSource) == noteId);
        }
    }
}
