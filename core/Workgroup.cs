using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        #region SearchContents
        /// <summary>
        /// Searches for the specified search entry within the specified search range. 
        /// If no skip count or take count are provided, it defaults to 0 and int.MaxValue respectively.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range to search within (Note or Workgroup).</param>
        /// <returns>The search result containing the found contents.</returns>
        public SearchResult SearchContents(string searchEntry, SearchRangeType searchRange)
        {
            return SearchContents(searchEntry, searchRange, 0, int.MaxValue);
        }

        /// <summary>
        /// Searches for the specified search entry within the specified search range with the specified skip and take counts.
        /// If the search range is a Note, it searches within the current selected note; otherwise, it searches within the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range to search within (Note or Workgroup).</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <returns>The search result containing the found contents.</returns>
        public SearchResult SearchContents(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteContents(searchEntry, skipCount, takeCount);
            else
                return SearchWorkgroupContents(searchEntry, skipCount, takeCount);
        }

        /// <summary>
        /// Searches for the specified search entry within the current selected note with the specified skip and take counts.
        /// If the current selected note is null, it returns an empty SearchResult.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <returns>The search result containing the found contents within the current selected note.</returns>
        public SearchResult SearchNoteContents(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchContents(searchEntry, skipCount, takeCount);
        }

        /// <summary>
        /// Searches for the specified search entry within the entire workgroup and aggregates the results from each note.
        /// The method creates a table of contents count for each note in the workgroup, then iterates over each note to fetch matching contents.
        /// If the skip count exceeds the total number of contents in a note, it moves on to the next note.
        /// The method returns a list of contents that match the search entry, along with the total count of matched contents.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <returns>A SearchResult object containing the found contents within the entire workgroup.</returns>
        public SearchResult SearchWorkgroupContents(string searchEntry, int skipCount, int takeCount)
        {
            DateTime startTime = DateTime.UtcNow;
            var tables = CreateContentsCountTable(searchEntry);
            IEnumerable<Content> contents = new List<Content>();
            foreach (var note in Notes)
            {
                int count = tables[note.DataSource];
                if (skipCount < count)
                {
                    var s = note.SearchContents(searchEntry, skipCount, takeCount);
                    contents = contents.Concat(s.Contents);

                    int newSkipCount, newTakeCount;
                    if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                    {
                        skipCount = newSkipCount;
                        takeCount = newTakeCount;
                    }
                    else
                        break;
                }
                else
                {
                    skipCount -= count; //read skip
                }
            }
            var sr = new SearchResult();
            sr.Contents = contents.ToList();
            sr.Count = tables.Select(kv => kv.Value).Sum();
            sr.StartTime = startTime;
            sr.EndTime = DateTime.UtcNow;
            return sr;
        }
        Dictionary<string, int> CreateContentsCountTable(string searchEntry)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach (var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.Where("Name"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
            }
            return tables;
        }
        #endregion

        #region SearchContentsAsync
        /// <summary>
        /// Asynchronously searches for the specified search entry within the specified search range with the default skip and take counts.
        /// If the search range is a Note, it searches within the current selected note; otherwise, it searches within the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range to search within (Note or Workgroup).</param>
        /// <param name="token">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task representing the asynchronous operation, which upon completion returns a SearchResult object containing the found contents.</returns>
        public Task<SearchResult> SearchContentsAsync(string searchEntry, SearchRangeType searchRange, CancellationToken token)
        {
            return SearchContentsAsync(searchEntry, searchRange, 0, int.MaxValue, token);
        }

        /// <summary>
        /// Asynchronously searches for the specified search entry within the specified search range with the specified skip and take counts, using a cancellation token to potentially cancel the operation.
        /// If the search range is a Note, it asynchronously searches within the current selected note; otherwise, it asynchronously searches within the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range to search within (Note or Workgroup).</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="token">A cancellation token that can be used to potentially cancel the asynchronous operation.</param>
        /// <returns>A task representing the asynchronous operation, which upon completion returns a SearchResult object containing the found contents.</returns>
        public Task<SearchResult> SearchContentsAsync(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount, CancellationToken token)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteContentsAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchWorkgroupContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        /// <summary>
        /// Asynchronously searches for the specified search entry within the contents of the currently selected note with the specified skip and take counts, using a cancellation token to potentially cancel the operation.
        /// If the currently selected note is null, it returns an empty search result.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="token">A cancellation token that can be used to potentially cancel the asynchronous operation.</param>
        /// <returns>A task representing the asynchronous operation, which upon completion returns a SearchResult object containing the found contents.</returns>
        public Task<SearchResult> SearchNoteContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
        }

        /// <summary>
        /// Asynchronously searches for the specified search entry within the contents of the entire workgroup with the specified skip and take counts, using a cancellation token to potentially cancel the operation.
        /// For each note in the workgroup, it retrieves the content count for the search entry and performs a search within the note if the skip count is less than the content count.
        /// If additional queries are needed, it adjusts the skip and take counts accordingly.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="token">A cancellation token that can be used to potentially cancel the asynchronous operation.</param>
        /// <returns>A task representing the asynchronous operation, which upon completion returns a SearchResult object containing the found contents within the entire workgroup.</returns>
        public Task<SearchResult> SearchWorkgroupContentsAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run(async () =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateContentsCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in Notes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchContentsAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                        {
                            skipCount = newSkipCount;
                            takeCount = newTakeCount;
                        }
                        else
                            break;
                    }
                    else
                    {
                        skipCount -= count; //read skip
                    }
                    CancelIfRequested(token);
                }
                var sr = new SearchResult();
                sr.Contents = contents.ToList();
                sr.Count = tables.Select(kv => kv.Value).Sum();
                sr.StartTime = startTime;
                sr.EndTime = DateTime.UtcNow;
                return sr;
            }, token);
            return task;
        }
        Dictionary<string, int> CreateContentsCountTable(string searchEntry, CancellationToken token)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach (var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("SELECT * FROM Contents ");
                    builder.AppendLine(textMatch.Where("Name"));
                    int count = db.Contents.FromSqlRaw(builder.ToString()).Count();
                    tables[dataSource] = count;
                }
                CancelIfRequested(token);
            }
            return tables;
        }
        #endregion

        #region SearchFullText
        public SearchResult SearchFullText(string searchEntry, SearchRangeType searchRange)
        {
            return SearchFullText(searchEntry, searchRange, 0, int.MaxValue);
        }
        /// <summary>
        /// Searches for the full text of the specified search entry within the specified search range with the provided skip and take counts.
        /// If the search range is set to SearchRangeType.Note, it searches for the full text within the currently selected note.
        /// If the search range is set to SearchRangeType.Workgroup, it searches for the full text within the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range within which to search for the full text.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <returns>A SearchResult object containing the found contents within the specified search range with the provided skip and take counts.</returns>
        public SearchResult SearchFullText(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteFullText(searchEntry, skipCount, takeCount);
            else
                return SearchWorkgroupFullText(searchEntry, skipCount, takeCount);
        }
        /// <summary>
        /// Searches for the full text of the specified search entry within the currently selected note with the provided skip and take counts.
        /// If the currently selected note is null, it returns an empty SearchResult object.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <returns>A SearchResult object containing the found contents within the currently selected note with the provided skip and take counts.</returns>
        public SearchResult SearchNoteFullText(string searchEntry, int skipCount, int takeCount)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return SearchResult.Empty;
            else
                return currentNote.SearchFullText(searchEntry, skipCount, takeCount);
        }
        public SearchResult SearchWorkgroupFullText(string searchEntry, int skipCount, int takeCount)
        {
            return SearchResult.Empty;
        }
        #endregion

        #region SearchFullTextAsync
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, SearchRangeType searchRange, CancellationToken token)
        {
            return SearchFullTextAsync(searchEntry, searchRange, 0, int.MaxValue, token);
        }

        /// <summary>
        /// Asynchronously searches for the full text of the specified search entry within the specified search range with the provided skip and take counts.
        /// If the search range is set to SearchRangeType.Note, it asynchronously searches for the full text within the currently selected note.
        /// If the search range is set to SearchRangeType.Workgroup, it asynchronously searches for the full text within the entire workgroup.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="searchRange">The range within which to search for the full text.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="token">The cancellation token to cancel the asynchronous operation if needed.</param>
        /// <returns>A task representing the asynchronous operation that returns a SearchResult object containing the found contents within the specified search range with the provided skip and take counts.</returns>
        public Task<SearchResult> SearchFullTextAsync(string searchEntry, SearchRangeType searchRange, int skipCount, int takeCount, CancellationToken token)
        {
            if (searchRange == SearchRangeType.Note)
                return SearchNoteFullTextAsync(searchEntry, skipCount, takeCount, token);
            else
                return SearchWorkgroupFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        public Task<SearchResult> SearchNoteFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var currentNote = this.SelectedNote;
            if (currentNote == null)
                return Task.Run(() => { return SearchResult.Empty; });
            else
                return currentNote.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
        }

        /// <summary>
        /// Asynchronously searches for the full text of the specified search entry within the entire workgroup, 
        /// aggregating results from all notes within the workgroup based on skip and take counts.
        /// The search is performed by querying the database tables asynchronously to retrieve the relevant contents.
        /// If the skip count exceeds the number of content items in a particular note, the search continues to the next note.
        /// Returns a task representing the asynchronous operation that provides a SearchResult object 
        /// containing the found contents within the workgroup with the total count and execution time.
        /// </summary>
        /// <param name="searchEntry">The search entry to look for.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="token">The cancellation token to cancel the asynchronous operation if needed.</param>
        /// <returns>A task representing the asynchronous operation that returns a SearchResult object containing the found contents within the workgroup with the total count and execution time.</returns>
        public Task<SearchResult> SearchWorkgroupFullTextAsync(string searchEntry, int skipCount, int takeCount, CancellationToken token)
        {
            var task = Task.Run(async () =>
            {
                DateTime startTime = DateTime.UtcNow;
                var tables = CreateFullTextCountTable(searchEntry, token);
                IEnumerable<Content> contents = new List<Content>();
                foreach (var note in Notes)
                {
                    int count = tables[note.DataSource];
                    if (skipCount < count)
                    {
                        var s = await note.SearchFullTextAsync(searchEntry, skipCount, takeCount, token);
                        contents = contents.Concat(s.Contents);

                        int newSkipCount, newTakeCount;
                        if (NeedMoreQuery(count, skipCount, takeCount, out newSkipCount, out newTakeCount))
                        {
                            skipCount = newSkipCount;
                            takeCount = newTakeCount;
                        }
                        else
                            break;
                    }
                    else
                    {
                        skipCount -= count; //read skip
                    }
                    CancelIfRequested(token);
                }
                var sr = new SearchResult();
                sr.Contents = contents.ToList();
                sr.Count = tables.Select(kv => kv.Value).Sum();
                sr.StartTime = startTime;
                sr.EndTime = DateTime.UtcNow;
                return sr;
            }, token);
            return task;
        }

        Dictionary<string, int> CreateFullTextCountTable(string searchEntry, CancellationToken token)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>();
            foreach (var n in Notes)
                tables.Add(n.DataSource, 0);

            TextMatching textMatch = TextMatching.Create(searchEntry);
            foreach (var dataSource in tables.Select(t => t.Key).ToList().AsParallel())
            {
                using (NoteDbContext db = new NoteDbContext(dataSource))
                {
                    if (!string.IsNullOrWhiteSpace(textMatch.Pattern))
                    {
                        StringBuilder builder = new StringBuilder();
                        builder.AppendLine(
                            "SELECT p.* FROM Pages p JOIN " +
                           $"(SELECT rowid FROM FtsIndex WHERE FtsIndex MATCH 'Text : \"{textMatch.Pattern}\"') f " +
                            "ON p.Rowid = f.rowid "
                        );
                        int count = db.Pages.FromSqlRaw(builder.ToString()).Count();
                        tables[dataSource] = count;
                    }
                    else
                    {
                        int count = db.Contents.Count();
                        tables[dataSource] = count;
                    }
                }
                CancelIfRequested(token);
            }
            return tables;
        }
        #endregion

        static bool CancelIfRequested(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }
            return true;
        }

        /// <summary>
        /// Determines if additional queries are needed based on the current results count, skip count, and take count.
        /// Calculates the new skip count and take count for the next query iteration if necessary.
        /// </summary>
        /// <param name="resultCount">The total count of results obtained so far.</param>
        /// <param name="skipCount">The number of items to skip before returning search results.</param>
        /// <param name="takeCount">The maximum number of items to include in the search results.</param>
        /// <param name="newSkipCount">The updated skip count for the next query iteration.</param>
        /// <param name="newTakeCount">The updated take count for the next query iteration.</param>
        /// <returns>True if more queries are needed, false otherwise.</returns>
        bool NeedMoreQuery(int resultCount, int skipCount, int takeCount, out int newSkipCount, out int newTakeCount)
        {
            if (resultCount < skipCount + takeCount)
            {
                newSkipCount = skipCount - resultCount;
                if (newSkipCount < 0)
                {
                    newTakeCount = newSkipCount + takeCount;
                    newSkipCount = 0;
                }
                else
                    newTakeCount = takeCount;

                return true;
            }
            else
            {
                newSkipCount = newTakeCount = 0;
                return false;
            }
        }

        /// <summary>
        /// Reads the specified content from the currently selected note and then iterates through other notes to find and read the content.
        /// <para>If the content is found in the currently selected note, it returns the page, otherwise it searches through other notes.</para>
        /// </summary>
        /// <param name="content">The content to read from the notes.</param>
        /// <returns>The page of the specified content if found in any of the notes, otherwise null.</returns>
        public Page ReadAll(IContent content)
        {
            Note current = this.SelectedNote;
            Page page = current.ReadPage(content.Guid);
            if (page != null)
                return page;

            foreach (var note in this.Notes.Where(n => !n.Equals(current)))
            {
                page = note.ReadPage(content.Guid);
                if (page != null)
                    return page;
            }
            return null;
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
        /// Checks if the selected note allows text editing, validates the text content, and returns the validation result.
        /// </summary>
        /// <param name="content">The content to be edited in the text.</param>
        /// <param name="testText">The updated content of the text to be edited.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text editing is valid, false otherwise.</returns>
        public bool ValidateEditText(IContent content, string testText, out List<string> errors)
        {
            errors = new List<string>();
            if (SelectedNote.Metadata.ReadOnly)
            {
                errors.Add("Edit text is not allowed.");
                return false;
            }
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }
            return TextUtil.ValidateTextString(testText, errors);
        }

        /// <summary>
        /// Validates the renaming of the text with the specified content name and updates the list of errors if validation fails.
        /// Checks if the selected note allows text renaming, validates the new text name, and checks if the name is already in use.
        /// </summary>
        /// <param name="content">The content of the text to be renamed.</param>
        /// <param name="testName">The new name for the text.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text renaming is valid, false otherwise.</returns>
        public bool ValidateRenameText(IContent content, string testName, out List<string> errors)
        {
            errors = new List<string>();
            if (SelectedNote.Metadata.ReadOnly)
            {
                errors.Add("Rename text is not allowed.");
                return false;
            }
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }
            TextUtil.ValidateNameString(testName, errors);
            if (SelectedNote.ReadPage(testName).FirstOrDefault() != null)
                errors.Add("The text name is already in use.");

            return errors.Count == 0;
        }

        /// <summary>
        /// Validates the deletion of the text content with the specified content and updates the list of errors if validation fails.
        /// Checks if the selected note allows text deletion, validates the content, and returns the validation result.
        /// </summary>
        /// <param name="content">The content to be deleted from the text.</param>
        /// <param name="errors">A list of error messages if validation fails.</param>
        /// <returns>True if the text deletion is valid, false otherwise.</returns>
        public bool ValidateDeleteText(IContent content, out List<string> errors)
        {
            errors = new List<string>();
            if (SelectedNote.Metadata.ReadOnly)
            {
                errors.Add("Delete text is not allowed.");
                return false;
            }
            if (content == null)
            {
                errors.Add("The text not yet opened.");
                return false;
            }
            return true;
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
        /// Validates if the selected note allows text editing, checks the validity of the text content, and updates the text content if validation passes.
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
                var page = SelectedNote.ReadPage(content);
                page.Text = newText;
                SelectedNote.UpdatePage(page);
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
        /// Validates if the selected note allows text renaming, checks the validity of the new text name, and verifies if the name is already in use.
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
                var page = SelectedNote.ReadPage(content);
                page.Name = newName;
                SelectedNote.UpdatePage(page);
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
                SelectedNote.DeletePage(content);
                mr.Content = null;
                mr.Notification = "The text deleted successfully.";
                mr.Result = true;
            }
            else
            {
                mr.Content = content.GetContent();
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
