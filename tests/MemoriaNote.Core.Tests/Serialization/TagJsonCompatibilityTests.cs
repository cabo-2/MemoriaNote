using MemoriaNote.Models;
using MemoriaNote.Persistence;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Serialization;

/// <summary>
/// Verifies compatibility of the persisted schema v1 tag JSON representation.
/// </summary>
[TestFixture]
public sealed class TagJsonCompatibilityTests
{
    const string ExistingTagJson =
        " { \"Dir\" : \"日本/雪\", \"Optional\" : null, " +
        "\"Escaped\" : \"quote: \\\" slash: / backslash: \\\\ newline: \\n\", " +
        "\"Combining\" : \"e\\u0301\" } ";

    static readonly Dictionary<string, string> ExpectedTags = new()
    {
        ["Dir"] = "日本/雪",
        ["Optional"] = null!,
        ["Escaped"] = "quote: \" slash: / backslash: \\ newline: \n",
        ["Combining"] = "e\u0301"
    };

    /// <summary>
    /// Verifies that existing page tag JSON round-trips the same dictionary data.
    /// </summary>
    [Test]
    public void PageTags_ExistingJson_RoundTripsSameData()
    {
        var page = new Page { Tags = ExistingTagJson };

        var serialized = page.Tags;
        var roundTripped = new Page { Tags = serialized };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TagDict, Is.EqualTo(ExpectedTags));
            Assert.That(roundTripped.TagDict, Is.EqualTo(ExpectedTags));
            Assert.That(serialized, Does.Contain("日本/雪"));
            Assert.That(serialized, Does.Not.Contain("\\u65E5"));
        }
    }

    /// <summary>
    /// Verifies that existing summary tag JSON uses the same compatibility codec.
    /// </summary>
    [Test]
    public void PageSummaryTags_ExistingJson_RoundTripsSameData()
    {
        var summary = new PageSummaryRecord { Tags = ExistingTagJson };

        var serialized = summary.Tags;
        var roundTripped = new PageSummaryRecord { Tags = serialized };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.TagDict, Is.EqualTo(ExpectedTags));
            Assert.That(roundTripped.TagDict, Is.EqualTo(ExpectedTags));
            Assert.That(serialized, Does.Contain("日本/雪"));
            Assert.That(serialized, Does.Not.Contain("\\u65E5"));
        }
    }

    /// <summary>
    /// Verifies all existing empty stored representations produce an empty dictionary.
    /// </summary>
    /// <param name="value">The stored tag value.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    public void EmptyTagJson_ProducesEmptyDictionary(string? value)
    {
        var page = new Page { Tags = value! };
        var summary = new PageSummaryRecord { Tags = value! };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TagDict, Is.Empty);
            Assert.That(page.Tags, Is.Null);
            Assert.That(summary.TagDict, Is.Empty);
            Assert.That(summary.Tags, Is.Null);
        }
    }

    /// <summary>
    /// Verifies a null dictionary retains the existing null stored representation.
    /// </summary>
    [Test]
    public void NullTagDictionary_ProducesNullJson()
    {
        var page = new Page { TagDict = null! };
        var summary = new PageSummaryRecord { TagDict = null! };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Tags, Is.Null);
            Assert.That(summary.Tags, Is.Null);
        }
    }
}
