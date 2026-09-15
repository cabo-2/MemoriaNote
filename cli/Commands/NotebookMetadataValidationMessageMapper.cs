using System;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Maps notebook metadata validation errors to existing CLI messages.
    /// </summary>
    internal static class NotebookMetadataValidationMessageMapper
    {
        /// <summary>Maps an error code to its existing presentation message.</summary>
        /// <param name="error">The machine-readable validation error.</param>
        /// <returns>The existing English error message.</returns>
        internal static string ToErrorMessage(NotebookMetadataErrorCode error)
        {
            return error switch
            {
                NotebookMetadataErrorCode.NameRequired => "Name cannot be blank",
                NotebookMetadataErrorCode.DuplicateName => "Name is already registered",
                NotebookMetadataErrorCode.TitleRequired => "Title cannot be blank",
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }
    }
}
