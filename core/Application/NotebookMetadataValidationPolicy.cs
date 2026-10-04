using System;
using System.Collections.Generic;

namespace MemoriaNote.Application
{
    /// <summary>
    /// Identifies a notebook metadata validation failure without presentation wording.
    /// </summary>
    public enum NotebookMetadataErrorCode
    {
        /// <summary>The notebook name is empty or whitespace.</summary>
        NameRequired,

        /// <summary>The notebook title is empty or whitespace.</summary>
        TitleRequired,

        /// <summary>The value contains a control character not supported by its field.</summary>
        ControlCharacter,

        /// <summary>The read-only value is not exactly true or false.</summary>
        InvalidReadOnlyValue
    }

    /// <summary>
    /// Applies notebook metadata rules that do not require persistence I/O.
    /// </summary>
    public sealed class NotebookMetadataValidationPolicy
    {
        /// <summary>Validates one proposed notebook metadata field value.</summary>
        /// <param name="field">The field being updated.</param>
        /// <param name="value">The proposed serialized value.</param>
        /// <returns>The first validation error, or an empty list.</returns>
        public IReadOnlyList<NotebookMetadataErrorCode> Validate(
            NotebookMetadataField field,
            string value)
        {
            if (!Enum.IsDefined(field))
                throw new ArgumentOutOfRangeException(nameof(field));

            if (field == NotebookMetadataField.Name && string.IsNullOrWhiteSpace(value))
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.NameRequired
                });
            }

            if (field == NotebookMetadataField.Title && string.IsNullOrWhiteSpace(value))
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.TitleRequired
                });
            }

            if (field == NotebookMetadataField.ReadOnly &&
                value != "true" &&
                value != "false")
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.InvalidReadOnlyValue
                });
            }

            if (ContainsUnsupportedControlCharacter(field, value))
            {
                return Array.AsReadOnly(new[]
                {
                    NotebookMetadataErrorCode.ControlCharacter
                });
            }

            return Array.Empty<NotebookMetadataErrorCode>();
        }

        static bool ContainsUnsupportedControlCharacter(
            NotebookMetadataField field,
            string value)
        {
            if (value == null || field == NotebookMetadataField.ReadOnly)
                return false;

            foreach (var character in value)
            {
                if (!char.IsControl(character))
                    continue;
                if (field == NotebookMetadataField.Description &&
                    (character == '\n' || character == '\r' || character == '\t'))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
