using System;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Maps page operation codes to CLI presentation messages.
    /// </summary>
    public static class PageOperationMessageMapper
    {
        /// <summary>
        /// Maps an error code to its CLI presentation message.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <param name="error">The machine-readable error code.</param>
        /// <returns>The English error message.</returns>
        public static string ToErrorMessage(
            PageOperationKind operation,
            PageErrorCode error)
        {
            return error switch
            {
                PageErrorCode.PageNotSelected => "No page is selected.",
                PageErrorCode.NameRequired => "A page name is required.",
                PageErrorCode.NameHasSurroundingWhitespace =>
                    "A page name cannot start or end with whitespace.",
                PageErrorCode.NameContainsControlCharacter =>
                    "A page name cannot contain control characters.",
                PageErrorCode.DuplicateName =>
                    "A page with this name already exists in the notebook.",
                PageErrorCode.OwnerNotFound => "The target notebook was not found.",
                PageErrorCode.PageNotFound => "The target page was not found.",
                PageErrorCode.ReadOnly => operation switch
                {
                    PageOperationKind.Create =>
                        "The notebook is read-only; a page cannot be created.",
                    PageOperationKind.Edit =>
                        "The notebook is read-only; the page cannot be edited.",
                    PageOperationKind.Rename =>
                        "The notebook is read-only; the page cannot be renamed.",
                    PageOperationKind.Delete =>
                        "The notebook is read-only; the page cannot be deleted.",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                },
                PageErrorCode.ConcurrentEdit =>
                    "The page changed while it was being edited. Run the command again " +
                    "to edit the latest content.",
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }

        /// <summary>
        /// Gets the success notification for an operation.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>The English success notification.</returns>
        public static string ToSuccessNotification(PageOperationKind operation)
        {
            return operation switch
            {
                PageOperationKind.Create => "Created the page.",
                PageOperationKind.Edit => "Updated the page.",
                PageOperationKind.Rename => "Renamed the page.",
                PageOperationKind.Delete => "Deleted the page.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Gets the failure notification for an operation.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>The English failure notification.</returns>
        public static string ToFailureNotification(PageOperationKind operation)
        {
            return operation switch
            {
                PageOperationKind.Create => "The page could not be created.",
                PageOperationKind.Edit => "The page could not be updated.",
                PageOperationKind.Rename => "The page could not be renamed.",
                PageOperationKind.Delete => "The page could not be deleted.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }
}
