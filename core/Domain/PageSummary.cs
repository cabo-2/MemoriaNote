using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemoriaNote
{
    /// <summary>
    /// Represents an immutable, body-free page summary with an explicit owning note.
    /// </summary>
    public sealed class PageSummary
    {
        readonly IReadOnlyDictionary<string, string> _tags;

        /// <summary>
        /// Initializes an immutable page summary.
        /// </summary>
        /// <param name="noteId">The identifier of the note that owns the page.</param>
        /// <param name="pageId">The stable page identifier.</param>
        /// <param name="name">The page name.</param>
        /// <param name="index">The one-based display index within an exact-name group.</param>
        /// <param name="tags">The page tags, copied when the summary is created.</param>
        /// <param name="contentType">The persisted content type.</param>
        /// <param name="createTime">The persisted creation time.</param>
        /// <param name="updateTime">The persisted last-update time.</param>
        /// <param name="isErased">Whether the page is marked as erased.</param>
        public PageSummary(
            NoteId noteId,
            PageId pageId,
            string name,
            int index,
            IReadOnlyDictionary<string, string> tags,
            string contentType,
            DateTime createTime,
            DateTime updateTime,
            bool isErased)
        {
            NoteId = noteId ?? throw new ArgumentNullException(nameof(noteId));
            PageId = pageId ?? throw new ArgumentNullException(nameof(pageId));
            Name = name;
            Index = index;
            ContentType = contentType;
            CreateTime = createTime;
            UpdateTime = updateTime;
            IsErased = isErased;

            var copiedTags = tags == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(tags);
            _tags = new ReadOnlyDictionary<string, string>(copiedTags);
        }

        /// <summary>
        /// Gets the identifier of the owning note.
        /// </summary>
        public NoteId NoteId { get; }

        /// <summary>
        /// Gets the stable page identifier.
        /// </summary>
        public PageId PageId { get; }

        /// <summary>
        /// Gets the page name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the one-based display index within an exact-name group.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets a read-only copy of the page tags.
        /// </summary>
        public IReadOnlyDictionary<string, string> Tags => _tags;

        /// <summary>
        /// Gets the persisted content type.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Gets the persisted creation time.
        /// </summary>
        public DateTime CreateTime { get; }

        /// <summary>
        /// Gets the persisted last-update time.
        /// </summary>
        public DateTime UpdateTime { get; }

        /// <summary>
        /// Gets a value indicating whether the page is marked as erased.
        /// </summary>
        public bool IsErased { get; }
    }
}
