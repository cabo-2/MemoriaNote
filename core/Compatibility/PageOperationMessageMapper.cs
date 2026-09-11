using System;

namespace MemoriaNote
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
            TextManageType operation,
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
                    TextManageType.Create => "Create text is not allowed.",
                    TextManageType.Edit => "Edit text is not allowed.",
                    TextManageType.Rename => "Rename text is not allowed.",
                    TextManageType.Delete => "Delete text is not allowed.",
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
        public static string ToSuccessNotification(TextManageType operation)
        {
            return operation switch
            {
                TextManageType.Create => "The text created successfully.",
                TextManageType.Edit => "The text updated successfully.",
                TextManageType.Rename => "The text renamed successfully.",
                TextManageType.Delete => "The text deleted successfully.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Gets the legacy failure notification for an operation.
        /// </summary>
        /// <param name="operation">The page operation.</param>
        /// <returns>The existing English failure notification.</returns>
        public static string ToFailureNotification(TextManageType operation)
        {
            return operation switch
            {
                TextManageType.Create => "Failed to create the text.",
                TextManageType.Edit => "Failed to update the text.",
                TextManageType.Rename => "Failed to rename the text.",
                TextManageType.Delete => "Failed to delete the text.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }
}
