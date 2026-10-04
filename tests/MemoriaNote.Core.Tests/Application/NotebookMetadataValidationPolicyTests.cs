using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>Verifies notebook metadata validation without presentation wording.</summary>
[TestFixture]
public sealed class NotebookMetadataValidationPolicyTests
{
    /// <summary>Verifies empty and whitespace display names are rejected.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Validate_EmptyName_ReturnsNameRequired(string? value)
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.Name,
            value!);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.NameRequired
        }));
    }

    /// <summary>Verifies empty and whitespace titles are rejected.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void Validate_EmptyTitle_ReturnsTitleRequired(string? value)
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.Title,
            value!);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.TitleRequired
        }));
    }

    /// <summary>Verifies display names have no workspace duplicate constraint.</summary>
    [Test]
    public void Validate_NonBlankName_HasNoDuplicateConstraint()
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.Name,
            "shared-name");

        Assert.That(errors, Is.Empty);
    }

    /// <summary>Verifies single-line fields reject every control character.</summary>
    [TestCase(NotebookMetadataField.Name)]
    [TestCase(NotebookMetadataField.Title)]
    [TestCase(NotebookMetadataField.Author)]
    [TestCase(NotebookMetadataField.Tag)]
    public void Validate_SingleLineFieldWithControlCharacter_ReturnsError(
        NotebookMetadataField field)
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            field,
            "before\tafter");

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.ControlCharacter
        }));
    }

    /// <summary>Verifies descriptions allow line and tab formatting.</summary>
    [Test]
    public void Validate_DescriptionWithFormattingControls_HasNoErrors()
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.Description,
            "first\r\nsecond\tvalue");

        Assert.That(errors, Is.Empty);
    }

    /// <summary>Verifies descriptions reject other control characters.</summary>
    [Test]
    public void Validate_DescriptionWithUnsupportedControl_ReturnsError()
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.Description,
            "before\0after");

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.ControlCharacter
        }));
    }

    /// <summary>Verifies read-only accepts only the documented lowercase values.</summary>
    [TestCase("true")]
    [TestCase("false")]
    public void Validate_ReadOnlyDocumentedValue_HasNoErrors(string value)
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.ReadOnly,
            value);

        Assert.That(errors, Is.Empty);
    }

    /// <summary>Verifies other read-only values are rejected.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("True")]
    [TestCase("writable")]
    public void Validate_InvalidReadOnly_ReturnsError(string? value)
    {
        var errors = new NotebookMetadataValidationPolicy().Validate(
            NotebookMetadataField.ReadOnly,
            value!);

        Assert.That(errors, Is.EqualTo(new[]
        {
            NotebookMetadataErrorCode.InvalidReadOnlyValue
        }));
    }
}
