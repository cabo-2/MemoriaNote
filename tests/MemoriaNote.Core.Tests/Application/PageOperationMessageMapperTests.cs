using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies compatibility message wording generated from typed operation codes.
/// </summary>
[TestFixture]
public sealed class PageOperationMessageMapperTests
{
    /// <summary>
    /// Verifies error messages remain compatible with the existing presentation contract.
    /// </summary>
    /// <param name="operation">The operation used for message context.</param>
    /// <param name="error">The typed error code.</param>
    /// <param name="expected">The expected legacy wording.</param>
    [TestCase(TextManageType.Edit, PageErrorCode.PageNotSelected, "The text not yet opened.")]
    [TestCase(TextManageType.Create, PageErrorCode.NameRequired, "The text name have not been entered.")]
    [TestCase(TextManageType.Rename, PageErrorCode.DuplicateName, "The text name is already in use.")]
    [TestCase(TextManageType.Delete, PageErrorCode.OwnerNotFound, "The text owner note was not found.")]
    [TestCase(TextManageType.Edit, PageErrorCode.PageNotFound, "The text was not found in its owner note.")]
    [TestCase(TextManageType.Create, PageErrorCode.ReadOnly, "Create text is not allowed.")]
    [TestCase(TextManageType.Edit, PageErrorCode.ReadOnly, "Edit text is not allowed.")]
    [TestCase(TextManageType.Rename, PageErrorCode.ReadOnly, "Rename text is not allowed.")]
    [TestCase(TextManageType.Delete, PageErrorCode.ReadOnly, "Delete text is not allowed.")]
    public void ToErrorMessage_ReturnsExistingWording(
        TextManageType operation,
        PageErrorCode error,
        string expected)
    {
        Assert.That(
            PageOperationMessageMapper.ToErrorMessage(operation, error),
            Is.EqualTo(expected));
    }

    /// <summary>
    /// Verifies operation notifications remain compatible with the existing presentation contract.
    /// </summary>
    /// <param name="operation">The operation to map.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="expected">The expected legacy wording.</param>
    [TestCase(TextManageType.Create, true, "The text created successfully.")]
    [TestCase(TextManageType.Edit, true, "The text updated successfully.")]
    [TestCase(TextManageType.Rename, true, "The text renamed successfully.")]
    [TestCase(TextManageType.Delete, true, "The text deleted successfully.")]
    [TestCase(TextManageType.Create, false, "Failed to create the text.")]
    [TestCase(TextManageType.Edit, false, "Failed to update the text.")]
    [TestCase(TextManageType.Rename, false, "Failed to rename the text.")]
    [TestCase(TextManageType.Delete, false, "Failed to delete the text.")]
    public void ToNotification_ReturnsExistingWording(
        TextManageType operation,
        bool success,
        string expected)
    {
        var notification = success
            ? PageOperationMessageMapper.ToSuccessNotification(operation)
            : PageOperationMessageMapper.ToFailureNotification(operation);

        Assert.That(notification, Is.EqualTo(expected));
    }
}
