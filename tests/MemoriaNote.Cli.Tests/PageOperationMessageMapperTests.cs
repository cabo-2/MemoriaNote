using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

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
    [TestCase(PageOperationKind.Edit, PageErrorCode.PageNotSelected, "The text not yet opened.")]
    [TestCase(PageOperationKind.Create, PageErrorCode.NameRequired, "The text name have not been entered.")]
    [TestCase(PageOperationKind.Rename, PageErrorCode.DuplicateName, "The text name is already in use.")]
    [TestCase(PageOperationKind.Delete, PageErrorCode.OwnerNotFound, "The text owner note was not found.")]
    [TestCase(PageOperationKind.Edit, PageErrorCode.PageNotFound, "The text was not found in its owner note.")]
    [TestCase(PageOperationKind.Create, PageErrorCode.ReadOnly, "Create text is not allowed.")]
    [TestCase(PageOperationKind.Edit, PageErrorCode.ReadOnly, "Edit text is not allowed.")]
    [TestCase(PageOperationKind.Rename, PageErrorCode.ReadOnly, "Rename text is not allowed.")]
    [TestCase(PageOperationKind.Delete, PageErrorCode.ReadOnly, "Delete text is not allowed.")]
    [TestCase(
        PageOperationKind.Edit,
        PageErrorCode.ConcurrentEdit,
        "The text changed after it was opened. Reopen it and try again.")]
    public void ToErrorMessage_ReturnsExistingWording(
        PageOperationKind operation,
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
    [TestCase(PageOperationKind.Create, true, "The text created successfully.")]
    [TestCase(PageOperationKind.Edit, true, "The text updated successfully.")]
    [TestCase(PageOperationKind.Rename, true, "The text renamed successfully.")]
    [TestCase(PageOperationKind.Delete, true, "The text deleted successfully.")]
    [TestCase(PageOperationKind.Create, false, "Failed to create the text.")]
    [TestCase(PageOperationKind.Edit, false, "Failed to update the text.")]
    [TestCase(PageOperationKind.Rename, false, "Failed to rename the text.")]
    [TestCase(PageOperationKind.Delete, false, "Failed to delete the text.")]
    public void ToNotification_ReturnsExistingWording(
        PageOperationKind operation,
        bool success,
        string expected)
    {
        var notification = success
            ? PageOperationMessageMapper.ToSuccessNotification(operation)
            : PageOperationMessageMapper.ToFailureNotification(operation);

        Assert.That(notification, Is.EqualTo(expected));
    }
}
