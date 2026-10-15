using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>
/// Verifies CLI message wording generated from typed operation codes.
/// </summary>
[TestFixture]
public sealed class PageOperationMessageMapperTests
{
    /// <summary>
    /// Verifies error messages use current CLI terminology and actionable wording.
    /// </summary>
    /// <param name="operation">The operation used for message context.</param>
    /// <param name="error">The typed error code.</param>
    /// <param name="expected">The expected wording.</param>
    [TestCase(PageOperationKind.Edit, PageErrorCode.PageNotSelected, "No page is selected.")]
    [TestCase(PageOperationKind.Create, PageErrorCode.NameRequired, "A page name is required.")]
    [TestCase(
        PageOperationKind.Create,
        PageErrorCode.NameContainsControlCharacter,
        "A page name cannot contain control characters.")]
    [TestCase(PageOperationKind.Rename, PageErrorCode.DuplicateName, "A page with this name already exists in the notebook.")]
    [TestCase(PageOperationKind.Delete, PageErrorCode.OwnerNotFound, "The target notebook was not found.")]
    [TestCase(PageOperationKind.Edit, PageErrorCode.PageNotFound, "The target page was not found.")]
    [TestCase(PageOperationKind.Create, PageErrorCode.ReadOnly, "The notebook is read-only; a page cannot be created.")]
    [TestCase(PageOperationKind.Edit, PageErrorCode.ReadOnly, "The notebook is read-only; the page cannot be edited.")]
    [TestCase(PageOperationKind.Rename, PageErrorCode.ReadOnly, "The notebook is read-only; the page cannot be renamed.")]
    [TestCase(PageOperationKind.Delete, PageErrorCode.ReadOnly, "The notebook is read-only; the page cannot be deleted.")]
    [TestCase(
        PageOperationKind.Edit,
        PageErrorCode.ConcurrentEdit,
        "The page changed while it was being edited. Run the command again to edit the latest content.")]
    public void ToErrorMessage_ReturnsCurrentWording(
        PageOperationKind operation,
        PageErrorCode error,
        string expected)
    {
        Assert.That(
            PageOperationMessageMapper.ToErrorMessage(operation, error),
            Is.EqualTo(expected));
    }

    /// <summary>
    /// Verifies operation notifications use page terminology.
    /// </summary>
    /// <param name="operation">The operation to map.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="expected">The expected wording.</param>
    [TestCase(PageOperationKind.Create, true, "Created the page.")]
    [TestCase(PageOperationKind.Edit, true, "Updated the page.")]
    [TestCase(PageOperationKind.Rename, true, "Renamed the page.")]
    [TestCase(PageOperationKind.Delete, true, "Deleted the page.")]
    [TestCase(PageOperationKind.Create, false, "The page could not be created.")]
    [TestCase(PageOperationKind.Edit, false, "The page could not be updated.")]
    [TestCase(PageOperationKind.Rename, false, "The page could not be renamed.")]
    [TestCase(PageOperationKind.Delete, false, "The page could not be deleted.")]
    public void ToNotification_ReturnsCurrentWording(
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
