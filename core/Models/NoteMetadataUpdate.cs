using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Describes metadata fields that should be persisted together.
    /// </summary>
    public sealed class NoteMetadataUpdate
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        internal IReadOnlyDictionary<string, string> Values => _values;

        /// <summary>
        /// Includes a note name change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetName(string value) => Set(NoteKeyValue.Name, value);

        /// <summary>
        /// Includes a note title change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetTitle(string value) => Set(NoteKeyValue.Title, value);

        /// <summary>
        /// Includes a note version change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetVersion(string value) => Set(NoteKeyValue.Version, value);

        /// <summary>
        /// Includes a note description change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetDescription(string value) =>
            Set(NoteKeyValue.Description, value);

        /// <summary>
        /// Includes a note author change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetAuthor(string value) => Set(NoteKeyValue.Author, value);

        /// <summary>
        /// Includes a read-only flag change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetReadOnly(bool value) =>
            Set(NoteKeyValue.ReadOnly, value.ToString());

        /// <summary>
        /// Includes a note tag change in this update.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetTag(string value) => Set(NoteKeyValue.Tag, value);

        /// <summary>
        /// Includes a creation time change using the existing 14-character storage format.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This update request.</returns>
        public NoteMetadataUpdate SetCreateTime(DateTime value) =>
            Set(NoteKeyValue.CreateTime, value.ToDateString());

        NoteMetadataUpdate Set(string key, string value)
        {
            _values[key] = value;
            return this;
        }
    }
}
