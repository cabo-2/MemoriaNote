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
