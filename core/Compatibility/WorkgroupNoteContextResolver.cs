using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Resolves application note contexts from the current workgroup collection.
    /// </summary>
    internal sealed class WorkgroupNoteContextResolver : INoteContextResolver
    {
        readonly Func<IEnumerable<Note>> _notes;

        internal WorkgroupNoteContextResolver(Func<IEnumerable<Note>> notes)
        {
            _notes = notes ?? throw new ArgumentNullException(nameof(notes));
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
            return _notes().FirstOrDefault(note =>
                NoteId.FromDataSource(note.DataSource) == noteId);
        }
    }
}
