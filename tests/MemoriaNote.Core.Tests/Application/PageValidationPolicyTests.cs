using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Application;

/// <summary>
/// Verifies page validation that does not require I/O.
/// </summary>
[TestFixture]
public sealed class PageValidationPolicyTests
{
    /// <summary>
    /// Verifies empty and whitespace names are rejected.
    /// </summary>
    /// <param name="name">The invalid name.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ValidateName_EmptyName_ReturnsNameRequired(string? name)
    {
        var result = new PageValidationPolicy().ValidateName(name!);

        Assert.That(result, Is.EqualTo(new[] { PageErrorCode.NameRequired }));
    }

    /// <summary>Verifies surrounding whitespace is rejected instead of being normalized.</summary>
    /// <param name="name">The invalid name.</param>
    [TestCase(" leading")]
    [TestCase("trailing ")]
    [TestCase("\tTabbed")]
    public void ValidateName_SurroundingWhitespace_ReturnsValidationError(string name)
    {
        var result = new PageValidationPolicy().ValidateName(name);

        Assert.That(
            result,
            Is.EqualTo(new[] { PageErrorCode.NameHasSurroundingWhitespace }));
    }

    /// <summary>Verifies page names are preserved without case or Unicode normalization.</summary>
    [TestCase("Meeting")]
    [TestCase("meeting")]
    [TestCase("Caf\u00e9")]
    [TestCase("Cafe\u0301")]
    public void ValidateName_ExactNonWhitespaceName_HasNoErrors(string name)
    {
        var result = new PageValidationPolicy().ValidateName(name);

        Assert.That(result, Is.Empty);
    }

    /// <summary>
    /// Verifies the existing policy continues to accept arbitrary page text.
    /// </summary>
    [Test]
    public void ValidateText_AnyText_HasNoErrors()
    {
        var policy = new PageValidationPolicy();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.ValidateText(null!), Is.Empty);
            Assert.That(policy.ValidateText(string.Empty), Is.Empty);
            Assert.That(policy.ValidateText("Unicode: 日本語 📝"), Is.Empty);
        }
    }
}
