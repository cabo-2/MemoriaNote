using MemoriaNote.Persistence.SchemaV2;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies the canonical Schema V2 tag representation.</summary>
[TestFixture]
public sealed class SchemaV2TagCodecTests
{
    /// <summary>Verifies duplicate removal, escaping, and UTF-8 byte ordering.</summary>
    [Test]
    public void Encode_CanonicalizesDuplicatesEscapesAndUtf8Order()
    {
        var encoded = SchemaV2TagCodec.Encode(
            ["𐀀", "quote\"slash\\", "日本語", "\uE000", "日本語"]);

        Assert.That(encoded,
            Is.EqualTo("[\"quote\\\"slash\\\\\",\"日本語\",\"\uE000\",\"𐀀\"]"));
        Assert.That(SchemaV2TagCodec.Decode(encoded),
            Is.EqualTo(new[] { "quote\"slash\\", "日本語", "\uE000", "𐀀" }));
        Assert.That(SchemaV2TagCodec.Encode([]), Is.EqualTo("[]"));
    }

    /// <summary>Verifies non-canonical JSON is rejected.</summary>
    [TestCase("{}")]
    [TestCase("[1]")]
    [TestCase("[\"tag\",\"tag\"]")]
    [TestCase("[\"𐀀\",\"\uE000\"]")]
    [TestCase("[ \"tag\" ]")]
    [TestCase("[\"\\u65e5\\u672c\\u8a9e\"]")]
    [TestCase("[\" padded \"]")]
    [TestCase("[\"\\u0001\"]")]
    public void Decode_RejectsInvalidOrNonCanonicalJson(string json)
    {
        Assert.That(
            () => SchemaV2TagCodec.Decode(json),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies labels outside the product contract are rejected.</summary>
    [Test]
    public void Encode_RejectsInvalidLabels()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => SchemaV2TagCodec.Encode([string.Empty]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => SchemaV2TagCodec.Encode([new string('x', 65)]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => SchemaV2TagCodec.Encode(["\ud800"]),
                Throws.TypeOf<ArgumentException>());
        }
    }
}
