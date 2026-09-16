using System;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Maps page operation codes to the legacy English presentation messages.
    /// </summary>
    public static class PageOperationMessageMapper
    {
        /// <summary>
        /// Maps an error code to its legacy presentation message.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <param name="error">The machine-readable error code.</param>
        /// <returns>The existing English error message.</returns>
        public static string ToErrorMessage(
            PageOperationKind operation,
            PageErrorCode error)
        {
            return error switch
            {
                PageErrorCode.PageNotSelected => "The text not yet opened.",
                PageErrorCode.NameRequired => "The text name have not been entered.",
                PageErrorCode.DuplicateName => "The text name is already in use.",
                PageErrorCode.OwnerNotFound => "The text owner note was not found.",
                PageErrorCode.PageNotFound => "The text was not found in its owner note.",
                PageErrorCode.ReadOnly => operation switch
                {
                    PageOperationKind.Create => "Create text is not allowed.",
                    PageOperationKind.Edit => "Edit text is not allowed.",
                    PageOperationKind.Rename => "Rename text is not allowed.",
                    PageOperationKind.Delete => "Delete text is not allowed.",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                },
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }

        /// <summary>
        /// Gets the legacy success notification for an operation.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>The existing English success notification.</returns>
        public static string ToSuccessNotification(PageOperationKind operation)
        {
            return operation switch
            {
                PageOperationKind.Create => "The text created successfully.",
                PageOperationKind.Edit => "The text updated successfully.",
                PageOperationKind.Rename => "The text renamed successfully.",
                PageOperationKind.Delete => "The text deleted successfully.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Gets the legacy failure notification for an operation.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>The existing English failure notification.</returns>
        public static string ToFailureNotification(PageOperationKind operation)
        {
            return operation switch
            {
                PageOperationKind.Create => "Failed to create the text.",
                PageOperationKind.Edit => "Failed to update the text.",
                PageOperationKind.Rename => "Failed to rename the text.",
                PageOperationKind.Delete => "Failed to delete the text.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }
}
