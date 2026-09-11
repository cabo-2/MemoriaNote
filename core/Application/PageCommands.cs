using System;

namespace MemoriaNote
{
    /// <summary>
    /// Identifies a page together with the note that owns it.
    /// </summary>
    public sealed class PageReference
    {
        /// <summary>
        /// Initializes an owner-qualified page reference.
        /// </summary>
        /// <param name="noteId">The identifier of the owning note.</param>
        /// <param name="pageId">The identifier of the page.</param>
        public PageReference(NoteId noteId, PageId pageId)
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            PageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
        }

        /// <summary>
        /// Gets the identifier of the owning note.
        /// </summary>
        public NoteId NoteId { get; }

        /// <summary>
        /// Gets the identifier of the page.
        /// </summary>
        public PageId PageId { get; }
    }

    /// <summary>
    /// Describes a request to create a page in an explicit note.
    /// </summary>
    public sealed class CreatePageCommand
    {
        /// <summary>
        /// Initializes a page creation command.
        /// </summary>
        /// <param name="noteId">The identifier of the target note.</param>
        /// <param name="name">The page name.</param>
        /// <param name="text">The page text.</param>
        /// <param name="directory">The optional page directory.</param>
        public CreatePageCommand(
            NoteId noteId,
            string name,
            string text,
            string directory = null)
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            Name = name;
            Text = text;
            Directory = directory;
        }

        /// <summary>
        /// Gets the identifier of the target note.
        /// </summary>
        public NoteId NoteId { get; }

        /// <summary>
        /// Gets the requested page name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the requested page text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the optional page directory.
        /// </summary>
        public string Directory { get; }
    }

    /// <summary>
    /// Describes a request to edit an owner-qualified page.
    /// </summary>
    public sealed class EditPageCommand
    {
        /// <summary>
        /// Initializes a page edit command.
        /// </summary>
        /// <param name="noteId">The identifier of the owning note.</param>
        /// <param name="pageId">The identifier of the page.</param>
        /// <param name="text">The replacement page text.</param>
        public EditPageCommand(NoteId noteId, PageId pageId, string text)
        {
            Target = new PageReference(noteId, pageId);
            Text = text;
        }

        /// <summary>
        /// Gets the owner-qualified page reference.
        /// </summary>
        public PageReference Target { get; }

        /// <summary>
        /// Gets the identifier of the owning note.
        /// </summary>
        public NoteId NoteId => Target.NoteId;

        /// <summary>
        /// Gets the identifier of the page.
        /// </summary>
        public PageId PageId => Target.PageId;

        /// <summary>
        /// Gets the replacement page text.
        /// </summary>
        public string Text { get; }
    }

    /// <summary>
    /// Describes a request to rename an owner-qualified page.
    /// </summary>
    public sealed class RenamePageCommand
    {
        /// <summary>
        /// Initializes a page rename command.
        /// </summary>
        /// <param name="noteId">The identifier of the owning note.</param>
        /// <param name="pageId">The identifier of the page.</param>
        /// <param name="name">The replacement page name.</param>
        public RenamePageCommand(NoteId noteId, PageId pageId, string name)
        {
            Target = new PageReference(noteId, pageId);
            Name = name;
        }

        /// <summary>
        /// Gets the owner-qualified page reference.
        /// </summary>
        public PageReference Target { get; }

        /// <summary>
        /// Gets the identifier of the owning note.
        /// </summary>
        public NoteId NoteId => Target.NoteId;

        /// <summary>
        /// Gets the identifier of the page.
        /// </summary>
        public PageId PageId => Target.PageId;

        /// <summary>
        /// Gets the replacement page name.
        /// </summary>
        public string Name { get; }
    }

    /// <summary>
    /// Describes a request to delete an owner-qualified page.
    /// </summary>
    public sealed class DeletePageCommand
    {
        /// <summary>
        /// Initializes a page deletion command.
        /// </summary>
        /// <param name="noteId">The identifier of the owning note.</param>
        /// <param name="pageId">The identifier of the page.</param>
        public DeletePageCommand(NoteId noteId, PageId pageId)
        {
            Target = new PageReference(noteId, pageId);
        }

        /// <summary>
        /// Gets the owner-qualified page reference.
        /// </summary>
        public PageReference Target { get; }

        /// <summary>
        /// Gets the identifier of the owning note.
        /// </summary>
        public NoteId NoteId => Target.NoteId;

        /// <summary>
        /// Gets the identifier of the page.
        /// </summary>
        public PageId PageId => Target.PageId;
    }
}
