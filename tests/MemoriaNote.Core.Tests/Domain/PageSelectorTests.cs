using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Domain;

/// <summary>Verifies user-facing page selector parsing.</summary>
[TestFixture]
public sealed class PageSelectorTests
{
    /// <summary>Verifies complete UUIDs are normalized for prefix lookup.</summary>
    [Test]
    public void TryFromPageId_CompleteUuid_NormalizesCaseAndHyphens()
    {
        var parsed = PageSelector.TryFromPageId(
            "ABCDEF01-2345-6789-ABCD-EF0123456789",
            out var selector);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector!.IsName, Is.False);
            Assert.That(
                selector.PageIdPrefix,
                Is.EqualTo("abcdef0123456789abcdef0123456789"));
        }
    }

    /// <summary>Verifies short hexadecimal prefixes are accepted from four characters.</summary>
    [TestCase("abcd", "abcd")]
    [TestCase("ABCDEF", "abcdef")]
    [TestCase("abcdef0123456789abcdef0123456789", "abcdef0123456789abcdef0123456789")]
    public void TryFromPageId_SupportedPrefix_ReturnsNormalizedSelector(
        string value,
        string expected)
    {
        var parsed = PageSelector.TryFromPageId(value, out var selector);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(selector?.PageIdPrefix, Is.EqualTo(expected));
        }
    }

    /// <summary>Verifies malformed or overly short Page IDs are rejected.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("abc")]
    [TestCase("abcd-efgh")]
    [TestCase("abcdef0123456789abcdef01234567890")]
    public void TryFromPageId_InvalidValue_ReturnsFalse(string? value)
    {
        var parsed = PageSelector.TryFromPageId(value, out var selector);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.False);
            Assert.That(selector, Is.Null);
        }
    }
}
