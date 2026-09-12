using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a workgroup that contains a collection of notes and provides methods to search for content within the workgroup or within a specific note.
    /// Implements the IWorkgroup interface without depending on presentation frameworks.
    /// </summary>
    public class Workgroup : IWorkgroup
    {
        /// <summary>
        /// Initializes an empty workgroup.
        /// </summary>
        public Workgroup() : this(null, Array.Empty<Note>())
        {
        }

        /// <summary>
        /// Initializes a workgroup with a defensive copy of its notes.
        /// </summary>
        /// <param name="name">The workgroup name.</param>
        /// <param name="notes">The notes contained in the workgroup.</param>
        /// <param name="selectedNote">The initially selected note, or null.</param>
        public Workgroup(
            string name,
            IEnumerable<Note> notes,
            Note selectedNote = null)
        {
            if (notes == null)
                throw new ArgumentNullException(nameof(notes));

            var copiedNotes = notes.ToList();
            if (copiedNotes.Any(note => note == null))
            {
                throw new ArgumentException(
                    "The workgroup cannot contain a null note.",
                    nameof(notes));
            }

            Name = name;
            _notes = new ReadOnlyCollection<Note>(copiedNotes);
            _compatibilityFacade = new WorkgroupCompatibilityFacade(
                () => _notes,
                () => _selectedNote);
            SelectNote(selectedNote);
        }

        Note _selectedNote;
        readonly IReadOnlyList<Note> _notes;
        readonly WorkgroupCompatibilityFacade _compatibilityFacade;

        #region Search
        /// <summary>
        /// Asynchronously searches the selected note or the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchRange">The range to search.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="token">The cancellation token for the search.</param>
        /// <returns>The matching contents and total count.</returns>
        public Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            CancellationToken token)
        {
            return SearchAsync(
                searchEntry,
                searchRange,
                searchMethod,
                0,
                int.MaxValue,
                token);
        }

        /// <summary>
        /// Asynchronously searches the selected note or the entire workgroup using paging values.
        /// </summary>
        /// <param name="searchEntry">The search entry to match.</param>
        /// <param name="searchRange">The range to search.</param>
        /// <param name="searchMethod">The search method to use.</param>
        /// <param name="skipCount">The number of matching contents to skip.</param>
        /// <param name="takeCount">The maximum number of matching contents to return.</param>
        /// <param name="token">The cancellation token for the search.</param>
        /// <returns>The matching contents and total count.</returns>
        public Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            return _compatibilityFacade.SearchAsync(
                searchEntry,
                searchRange,
                searchMethod,
                skipCount,
                takeCount,
                token);
        }

        #endregion

        /// <summary>
        /// Reads the specified content from the note identified by its owner data source.
        /// </summary>
        /// <param name="content">The content to read from the notes.</param>
        /// <returns>The page of the specified content if found in any of the notes, otherwise null.</returns>
        public Page ReadAll(IContent content)
        {
            return _compatibilityFacade.Read(
                WorkgroupCompatibilityFacade.CreateReference(content));
        }

        /// <summary>
        /// Validates the creation of a new text with the specified name and text content.
        /// Checks if the selected note allows text creation, validates the text name, and checks if the name is already in use.
        /// </summary>
        /// <param name="testName">The name of the text to be created.</param>
        /// <param name="testText">The content of the text to be created.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text creation is valid, false otherwise.</returns>
        public bool ValidateCreateText(string testName, string testText, out List<string> errors)
        {
            var result = _compatibilityFacade.ValidateCreate(
                _compatibilityFacade.CreateCreateCommand(testName, testText));
            errors = WorkgroupCompatibilityFacade.ToErrorMessages(
                TextManageType.Create,
                result);
            return result.IsSuccess;
        }

        /// <summary>
        /// Validates the editing of the text content with the specified content and updates the list of errors if validation fails.
        /// Checks if the owning note allows text editing, validates the text content, and returns the validation result.
        /// </summary>
        /// <param name="content">The content to be edited in the text.</param>
        /// <param name="testText">The updated content of the text to be edited.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text editing is valid, false otherwise.</returns>
        public bool ValidateEditText(IContent content, string testText, out List<string> errors)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new EditPageCommand(target.NoteId, target.PageId, testText);
            var result = _compatibilityFacade.ValidateEdit(command);
            errors = WorkgroupCompatibilityFacade.ToErrorMessages(
                TextManageType.Edit,
                result);
            return result.IsSuccess;
        }

        /// <summary>
        /// Validates the renaming of the text with the specified content name and updates the list of errors if validation fails.
        /// Checks if the owning note allows text renaming, validates the new text name, and checks if the name is already in use there.
        /// </summary>
        /// <param name="content">The content of the text to be renamed.</param>
        /// <param name="testName">The new name for the text.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text renaming is valid, false otherwise.</returns>
        public bool ValidateRenameText(IContent content, string testName, out List<string> errors)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new RenamePageCommand(target.NoteId, target.PageId, testName);
            var result = _compatibilityFacade.ValidateRename(command);
            errors = WorkgroupCompatibilityFacade.ToErrorMessages(
                TextManageType.Rename,
                result);
            return result.IsSuccess;
        }

        /// <summary>
        /// Validates the deletion of the text content with the specified content and updates the list of errors if validation fails.
        /// Checks if the owning note allows text deletion, validates the content, and returns the validation result.
        /// </summary>
        /// <param name="content">The content to be deleted from the text.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text deletion is valid, false otherwise.</returns>
        public bool ValidateDeleteText(IContent content, out List<string> errors)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new DeletePageCommand(target.NoteId, target.PageId);
            var result = _compatibilityFacade.ValidateDelete(command);
            errors = WorkgroupCompatibilityFacade.ToErrorMessages(
                TextManageType.Delete,
                result);
            return result.IsSuccess;
        }

        /// <summary>
        /// Creates a new text with the specified name and text content.
        /// Validates if the selected note allows text creation, checks the validity of the text name, and verifies if the name is already in use.
        /// </summary>
        /// <param name="newName">The name of the text to be created.</param>
        /// <param name="newText">The content of the text to be created.</param>
        /// <returns>A TextManageResult indicating the result of the text creation operation.</returns>
        public TextManageResult CreateText(string newName, string newText)
        {
            return _compatibilityFacade.Create(
                _compatibilityFacade.CreateCreateCommand(newName, newText));
        }

        /// <summary>
        /// Updates the content of a text with the specified new text content.
        /// Validates if the owning note allows text editing, checks the text content, and updates that note if validation passes.
        /// </summary>
        /// <param name="content">The content of the text to be edited.</param>
        /// <param name="newText">The new content for the text.</param>
        /// <returns>A TextManageResult indicating the result of the text editing operation.</returns>
        public TextManageResult EditText(IContent content, string newText)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new EditPageCommand(target.NoteId, target.PageId, newText);
            return _compatibilityFacade.Edit(command);
        }

        /// <summary>
        /// Renames the text content with the specified new name and updates the list of errors if validation fails.
        /// Validates if the owning note allows text renaming and whether the new name is already in use there.
        /// </summary>
        /// <param name="content">The content of the text to be renamed.</param>
        /// <param name="newName">The new name for the text.</param>
        /// <returns>A TextManageResult indicating the result of the text renaming operation.</returns>
        public TextManageResult RenameText(IContent content, string newName)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new RenamePageCommand(target.NoteId, target.PageId, newName);
            return _compatibilityFacade.Rename(command);
        }

        /// <summary>
        /// Deletes the text content specified by the input content parameter.
        /// Validates if the selected note allows text deletion, checks the validity of the content, and updates the list of errors if validation fails.
        /// If validation passes, deletes the text content and returns a TextManageResult indicating the result of the delete operation.
        /// </summary>
        /// <param name="content">The content of the text to be deleted.</param>
        /// <returns>A TextManageResult indicating the result of the text deletion operation.</returns>
        public TextManageResult DeleteText(IContent content)
        {
            var target = WorkgroupCompatibilityFacade.CreateReference(content);
            var command = target == null
                ? null
                : new DeletePageCommand(target.NoteId, target.PageId);
            return _compatibilityFacade.Delete(command, content);
        }

        /// <summary>
        /// Gets the collection of notes stored in the application.
        /// </summary>
        /// <returns>A read-only list containing all the notes.</returns>
        public IReadOnlyList<Note> Notes => _notes;

        /// <summary>
        /// Retrieves a list of data sources used by the notes in the application.
        /// </summary>
        /// <returns>A list of strings representing the data sources used by the notes.</returns>
        public List<string> UseDataSources => _notes.Select(note => note.DataSource).ToList();

        /// <summary>
        /// Gets the currently selected note in the application.
        /// </summary>
        public Note SelectedNote => _selectedNote;

        /// <summary>
        /// Selects a note contained in this workgroup, or clears the selection.
        /// </summary>
        /// <param name="note">The note to select, or null to clear the selection.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="note"/> does not belong to this workgroup.
        /// </exception>
        public void SelectNote(Note note)
        {
            if (note == null)
            {
                _selectedNote = null;
                return;
            }

            var noteId = NoteId.FromDataSource(note.DataSource);
            var ownedNote = _notes.FirstOrDefault(candidate =>
                NoteId.FromDataSource(candidate.DataSource) == noteId);
            if (ownedNote == null)
            {
                throw new ArgumentException(
                    "The selected note must belong to the workgroup.",
                    nameof(note));
            }

            _selectedNote = ownedNote;
        }

        /// <summary>
        /// Gets the name of the currently selected note.
        /// </summary>
        /// <returns>A string representing the name of the currently selected note.</returns>
        public string SelectedNoteName => SelectedNote?.ToString();

        /// <summary>
        /// Gets or sets the workgroup name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Overrides the default ToString method to return the name of the text content if it is not null.
        /// If the name is null, the base ToString method result is returned.
        /// </summary>
        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }
}
