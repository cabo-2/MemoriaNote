using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Describes metadata fields that should be persisted together.
    /// </summary>
    public sealed class NotebookMetadataPatch
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        internal IReadOnlyDictionary<string, string> Values => _values;

        /// <summary>
        /// Creates a patch containing only values that differ from a persisted snapshot.
        /// </summary>
        /// <param name="persisted">The persisted metadata snapshot.</param>
        /// <param name="proposed">The proposed metadata values.</param>
        /// <returns>A patch containing the changed fields.</returns>
        public static NotebookMetadataPatch Create(
            NotebookMetadata persisted,
            NotebookMetadataUpdate proposed)
        {
            if (persisted == null)
                throw new ArgumentNullException(nameof(persisted));
            if (proposed == null)
                throw new ArgumentNullException(nameof(proposed));

            var patch = new NotebookMetadataPatch();
            if (!string.Equals(persisted.Name, proposed.Name, StringComparison.Ordinal))
                patch.SetName(proposed.Name);
            if (!string.Equals(persisted.Title, proposed.Title, StringComparison.Ordinal))
                patch.SetTitle(proposed.Title);
            if (!string.Equals(persisted.Version, proposed.Version, StringComparison.Ordinal))
                patch.SetVersion(proposed.Version);
            if (!string.Equals(
                persisted.Description,
                proposed.Description,
                StringComparison.Ordinal))
            {
                patch.SetDescription(proposed.Description);
            }
            if (!string.Equals(persisted.Author, proposed.Author, StringComparison.Ordinal))
                patch.SetAuthor(proposed.Author);
            if (persisted.ReadOnly != proposed.ReadOnly)
                patch.SetReadOnly(proposed.ReadOnly);
            if (!string.Equals(persisted.Tag, proposed.Tag, StringComparison.Ordinal))
                patch.SetTag(proposed.Tag);
            if (persisted.CreateTime != proposed.CreateTime)
                patch.SetCreateTime(proposed.CreateTime);

            return patch;
        }

        /// <summary>
        /// Includes a notebook name change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetName(string value) => Set(NoteKeyValue.Name, value);

        /// <summary>
        /// Includes a notebook title change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetTitle(string value) => Set(NoteKeyValue.Title, value);

        /// <summary>
        /// Includes a notebook version change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetVersion(string value) => Set(NoteKeyValue.Version, value);

        /// <summary>
        /// Includes a notebook description change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetDescription(string value) =>
            Set(NoteKeyValue.Description, value);

        /// <summary>
        /// Includes a notebook author change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetAuthor(string value) => Set(NoteKeyValue.Author, value);

        /// <summary>
        /// Includes a read-only flag change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetReadOnly(bool value) =>
            Set(NoteKeyValue.ReadOnly, value.ToString());

        /// <summary>
        /// Includes a notebook tag change in this patch.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetTag(string value) => Set(NoteKeyValue.Tag, value);

        /// <summary>
        /// Includes a creation time change using the existing 14-character storage format.
        /// </summary>
        /// <param name="value">The value to persist.</param>
        /// <returns>This patch request.</returns>
        public NotebookMetadataPatch SetCreateTime(DateTime value) =>
            Set(NoteKeyValue.CreateTime, value.ToDateString());

        NotebookMetadataPatch Set(string key, string value)
        {
            _values[key] = value;
            return this;
        }
    }
}
