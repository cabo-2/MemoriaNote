using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook metadata validation messages remain compatible.</summary>
[TestFixture]
public sealed class NotebookMetadataValidationMessageMapperTests
{
    /// <summary>Verifies typed validation errors use the existing CLI wording.</summary>
    /// <param name="error">The typed validation error.</param>
    /// <param name="expected">The expected existing message.</param>
    [TestCase(
        NotebookMetadataErrorCode.NameRequired,
        "Name cannot be blank")]
    [TestCase(
        NotebookMetadataErrorCode.DuplicateName,
        "Name is already registered")]
    [TestCase(
        NotebookMetadataErrorCode.TitleRequired,
        "Title cannot be blank")]
    public void ToErrorMessage_ReturnsExistingWording(
        NotebookMetadataErrorCode error,
        string expected)
    {
        Assert.That(
            NotebookMetadataValidationMessageMapper.ToErrorMessage(error),
            Is.EqualTo(expected));
    }
}
