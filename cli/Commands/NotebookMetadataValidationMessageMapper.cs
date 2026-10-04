using System;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Maps notebook metadata validation errors to CLI messages.
    /// </summary>
    internal static class NotebookMetadataValidationMessageMapper
    {
        /// <summary>Maps an error code to its presentation message.</summary>
        /// <param name="error">The machine-readable validation error.</param>
        /// <returns>The English error message.</returns>
        internal static string ToErrorMessage(NotebookMetadataErrorCode error)
        {
            return error switch
            {
                NotebookMetadataErrorCode.NameRequired => "Name cannot be blank",
                NotebookMetadataErrorCode.TitleRequired => "Title cannot be blank",
                NotebookMetadataErrorCode.ControlCharacter =>
                    "The metadata value contains an unsupported control character.",
                NotebookMetadataErrorCode.InvalidReadOnlyValue =>
                    "Read-only must be true or false.",
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }
    }
}
