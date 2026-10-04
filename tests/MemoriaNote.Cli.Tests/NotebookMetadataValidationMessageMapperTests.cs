using NUnit.Framework;

namespace MemoriaNote.Cli.Tests;

/// <summary>Verifies notebook metadata validation messages.</summary>
[TestFixture]
public sealed class NotebookMetadataValidationMessageMapperTests
{
    /// <summary>Verifies typed validation errors use stable CLI wording.</summary>
    /// <param name="error">The typed validation error.</param>
    /// <param name="expected">The expected message.</param>
    [TestCase(
        NotebookMetadataErrorCode.NameRequired,
        "Name cannot be blank")]
    [TestCase(
        NotebookMetadataErrorCode.TitleRequired,
        "Title cannot be blank")]
    [TestCase(
        NotebookMetadataErrorCode.ControlCharacter,
        "The metadata value contains an unsupported control character.")]
    [TestCase(
        NotebookMetadataErrorCode.InvalidReadOnlyValue,
        "Read-only must be true or false.")]
    public void ToErrorMessage_ReturnsStableWording(
        NotebookMetadataErrorCode error,
        string expected)
    {
        Assert.That(
            NotebookMetadataValidationMessageMapper.ToErrorMessage(error),
            Is.EqualTo(expected));
    }
}
