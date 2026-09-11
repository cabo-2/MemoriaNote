using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;

using System.Collections.ObjectModel;
using System.IO;
using ReactiveUI.Fody.Helpers;
using DynamicData;
using DynamicData.Binding;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a workgroup that contains a collection of notes and provides methods to search for content within the workgroup or within a specific note.
    /// Implements the IWorkgroup interface and inherits from ReactiveObject for property change notification.
    /// </summary>
    public class Workgroup : ReactiveObject, IWorkgroup
    {
        public Workgroup()
        {
            _notes = new ObservableCollectionExtended<Note>();
            _notes.CollectionChanged += (sender, e) => { this.RaisePropertyChanged(nameof(SelectedNoteIndex)); };
        }

        protected Note _selectedNote;
        protected ObservableCollectionExtended<Note> _notes;

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
        public async Task<SearchResult> SearchAsync(
            string searchEntry,
            SearchRangeType searchRange,
            SearchMethodType searchMethod,
            int skipCount,
            int takeCount,
            CancellationToken token)
        {
            if (!Enum.IsDefined(typeof(SearchRangeType), searchRange))
                throw new ArgumentOutOfRangeException(nameof(searchRange));

            var startTime = DateTime.UtcNow;
            var request = searchRange == SearchRangeType.Note
                ? SearchRequest.ForNote(
                    searchEntry,
                    searchMethod,
                    SelectedNote == null
                        ? null
                        : NoteId.FromDataSource(SelectedNote.DataSource),
                    skipCount,
                    takeCount)
                : SearchRequest.ForWorkgroup(
                    searchEntry,
                    searchMethod,
                    Notes.Select(note => NoteId.FromDataSource(note.DataSource)),
                    skipCount,
                    takeCount);
            var result = await new SearchUseCase(ResolveSearchRepository)
                .SearchAsync(request, token)
                .ConfigureAwait(false);
            return new SearchResult()
            {
                Contents = result.Items
                    .Select(ToCompatibilityContent)
                    .ToList(),
                Count = result.TotalCount,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        INoteSearchRepository ResolveSearchRepository(NoteId noteId)
        {
            return FindSearchNote(noteId)?.SearchRepository;
        }

        Content ToCompatibilityContent(PageSummary summary)
        {
            var content = PageSummaryContentAdapter.ToContent(summary);
            content.Parent = FindSearchNote(summary.NoteId);
            return content;
        }

        Note FindSearchNote(NoteId noteId)
        {
            var note = Notes.FirstOrDefault(candidate =>
                NoteId.FromDataSource(candidate.DataSource) == noteId);
            if (note != null)
                return note;

            if (SelectedNote != null &&
                NoteId.FromDataSource(SelectedNote.DataSource) == noteId)
                return SelectedNote;

            return null;
        }

        #endregion

        /// <summary>
        /// Reads the specified content from the note identified by its owner data source.
        /// </summary>
        /// <param name="content">The content to read from the notes.</param>
        /// <returns>The page of the specified content if found in any of the notes, otherwise null.</returns>
        public Page ReadAll(IContent content)
        {
            var owner = FindOwner(content);
            return owner?.ReadPage(content.Guid);
        }

        private Note FindOwner(IContent content)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.OwnerDataSource))
                return null;

            var ownerDataSource = Path.GetFullPath(content.OwnerDataSource);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return Notes.FirstOrDefault(note =>
                string.Equals(Path.GetFullPath(note.DataSource), ownerDataSource, comparison));
        }

        private bool ValidateOwnedContent(
            IContent content,
            string permissionError,
            out Note owner,
            out List<string> errors)
        {
            errors = new List<string>();
            owner = null;

            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }

            owner = FindOwner(content);
            if (owner == null)
            {
                errors.Add("The text owner note was not found.");
                return false;
            }

            if (owner.Metadata.ReadOnly)
            {
                errors.Add(permissionError);
                return false;
            }

            if (owner.ReadPage(content.Guid) == null)
            {
                errors.Add("The text was not found in its owner note.");
                return false;
            }

            return true;
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
            errors = new List<string>();
            if (SelectedNote.Metadata.ReadOnly)
            {
                errors.Add("Create text is not allowed.");
                return false;
            }
            TextUtil.ValidateNameString(testName, errors);
            if (SelectedNote.ReadPage(testName).FirstOrDefault() != null)
                errors.Add("The text name is already in use.");

            return errors.Count == 0;
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
            if (!ValidateOwnedContent(
                content,
                "Edit text is not allowed.",
                out _,
                out errors))
                return false;

            return TextUtil.ValidateTextString(testText, errors);
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
            if (!ValidateOwnedContent(
                content,
                "Rename text is not allowed.",
                out var owner,
                out errors))
                return false;

            TextUtil.ValidateNameString(testName, errors);
            if (owner.ReadPage(testName).FirstOrDefault() != null)
                errors.Add("The text name is already in use.");

            return errors.Count == 0;
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
            return ValidateOwnedContent(
                content,
                "Delete text is not allowed.",
                out _,
                out errors);
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
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Create };
            List<string> errors;
            var validate = ValidateCreateText(newName, newText, out errors);
            mr.Errors = errors;
            if (validate)
            {
                var page = SelectedNote.CreatePage(newName, newText);
                mr.Content = page.GetContent();
                mr.Notification = "The text created successfully.";
                mr.Result = true;
            }
            else
            {
                mr.Notification = "Failed to create the text.";
            }
            return mr;
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
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Edit };
            List<string> errors;
            var validate = ValidateEditText(content, newText, out errors);
            mr.Errors = errors;
            if (validate)
            {
                var owner = FindOwner(content);
                var page = owner.ReadPage(content);
                page.Text = newText;
                owner.UpdatePage(page);
                mr.Content = page.GetContent();
                mr.Notification = "The text updated successfully.";
                mr.Result = true;
            }
            else
            {
                mr.Notification = "Failed to update the text.";
            }
            return mr;
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
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Rename };
            List<string> errors;
            var validate = ValidateRenameText(content, newName, out errors);
            mr.Errors = errors;
            if (validate)
            {
                var owner = FindOwner(content);
                var page = owner.ReadPage(content);
                page.Name = newName;
                owner.UpdatePage(page);
                mr.Content = page.GetContent();
                mr.Notification = "The text renamed successfully.";
                mr.Result = true;
            }
            else
            {
                mr.Notification = "Failed to rename the text.";
            }
            return mr;
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
            TextManageResult mr = new TextManageResult() { Operation = TextManageType.Delete };
            List<string> errors;
            var validate = ValidateDeleteText(content, out errors);
            mr.Errors = errors;
            if (validate)
            {
                var owner = FindOwner(content);
                var page = owner.ReadPage(content);
                owner.DeletePage(page);
                mr.Content = null;
                mr.Notification = "The text deleted successfully.";
                mr.Result = true;
            }
            else
            {
                mr.Content = content?.GetContent();
                mr.Notification = "Failed to delete the text.";
            }
            return mr;
        }

        /// <summary>
        /// Gets the collection of notes stored in the application.
        /// </summary>
        /// <returns>An ObservableCollectionExtended containing all the notes.</returns>
        public ObservableCollectionExtended<Note> Notes => _notes;

        /// <summary>
        /// Retrieves a list of data sources used by the notes in the application.
        /// </summary>
        /// <returns>A list of strings representing the data sources used by the notes.</returns>
        public List<string> UseDataSources => _notes.Select(note => note.DataSource).ToList();

        /// <summary>
        /// Gets or sets the currently selected note in the application.
        /// If the selected note is changed, raises property changed events for the SelectedNoteIndex property.
        /// </summary>
        public Note SelectedNote
        {
            get => _selectedNote;
            set
            {
                if (!object.Equals(_selectedNote, value))
                {
                    this.RaiseAndSetIfChanged(ref _selectedNote, value);
                    this.RaisePropertyChanged(nameof(SelectedNoteIndex));
                }
            }
        }
        /// <summary>
        /// Gets or sets the index of the currently selected note in the application.
        /// If the selected note index is changed, sets the SelectedNote property to the note at the specified index in the list of notes.
        /// </summary>
        public int SelectedNoteIndex
        {
            get => _notes.IndexOf(_selectedNote);
            set => SelectedNote = _notes[value];
        }
        /// <summary>
        /// Gets the name of the currently selected note.
        /// </summary>
        /// <returns>A string representing the name of the currently selected note.</returns>
        public string SelectedNoteName => SelectedNote.ToString();

        /// <summary>
        /// Gets or sets the name of the text content.
        /// If the name is changed, raises property changed events for the Name property.
        /// Overrides the ToString method to return the name if it is not null, otherwise returns the base ToString method result.
        /// </summary>
        [Reactive] public string Name { get; set; }

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
