using System;

namespace MemoriaNote
{
    /// <summary>
    /// Provides the current metadata snapshot and persistence ports for one note.
    /// </summary>
    public sealed class NoteContext
    {
        /// <summary>
        /// Initializes a resolved note context.
        /// </summary>
        /// <param name="noteId">The resolved note identifier.</param>
        /// <param name="isReadOnly">Whether the current metadata snapshot is read-only.</param>
        /// <param name="pageRepository">The page repository.</param>
        /// <param name="searchRepository">The optional search repository.</param>
        public NoteContext(
            NoteId noteId,
            bool isReadOnly,
            INoteRepository pageRepository,
            INoteSearchRepository searchRepository = null)
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            IsReadOnly = isReadOnly;
            PageRepository = pageRepository ??
                throw new ArgumentNullException(nameof(pageRepository));
            SearchRepository = searchRepository;
        }

        /// <summary>
        /// Gets the resolved note identifier.
        /// </summary>
        public NoteId NoteId { get; }

        /// <summary>
        /// Gets whether the current metadata snapshot is read-only.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        /// Gets the page persistence port.
        /// </summary>
        public INoteRepository PageRepository { get; }

        /// <summary>
        /// Gets the optional search persistence port.
        /// </summary>
        public INoteSearchRepository SearchRepository { get; }
    }

    /// <summary>
    /// Resolves a typed note identifier against the current note collection.
    /// </summary>
    public interface INoteContextResolver
    {
        /// <summary>
        /// Resolves the current note context.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <returns>The current context, or null when the note is not available.</returns>
        NoteContext Resolve(NoteId noteId);
    }
}
