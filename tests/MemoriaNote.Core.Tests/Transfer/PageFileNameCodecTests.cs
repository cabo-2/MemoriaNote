using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Transfer;

/// <summary>
/// Verifies portable page file-name encoding.
/// </summary>
[TestFixture]
public sealed class PageFileNameCodecTests
{
    /// <summary>
    /// Verifies names that are portable remain readable and unchanged.
    /// </summary>
    /// <param name="pageName">The portable page name.</param>
    [TestCase("Meeting Notes")]
    [TestCase("日本語の名前")]
    [TestCase("100% complete")]
    [TestCase("COM0")]
    [TestCase("LPT0")]
    [TestCase("CLOCKS$")]
    [TestCase("Literal／Slash")]
    public void Encode_PortableName_ReturnsOriginalName(string pageName)
    {
        var result = new PageFileNameCodec().Encode(pageName);

        Assert.That(result, Is.EqualTo(pageName));
    }

    /// <summary>
    /// Verifies unsafe characters use the versioned percent-escape format.
    /// </summary>
    /// <param name="pageName">The unsafe page name.</param>
    /// <param name="expected">The expected portable component.</param>
    [TestCase("A/B", "A%2FB~mn~2~")]
    [TestCase("A\\B", "A%5CB~mn~2~")]
    [TestCase("A:B", "A%3AB~mn~2~")]
    [TestCase("A*B?", "A%2AB%3F~mn~2~")]
    [TestCase("A\"B", "A%22B~mn~2~")]
    [TestCase("A<B>|", "A%3CB%3E%7C~mn~2~")]
    [TestCase("Line\nBreak", "Line%0ABreak~mn~2~")]
    [TestCase("Trailing. ", "Trailing%2E%20~mn~2~")]
    public void Encode_UnsafeName_UsesCanonicalEscape(
        string pageName,
        string expected)
    {
        var result = new PageFileNameCodec().Encode(pageName);

        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Verifies Windows device names are detected case-insensitively and before extensions.
    /// </summary>
    /// <param name="pageName">The reserved page name.</param>
    [TestCase("CON")]
    [TestCase("con.report")]
    [TestCase("PRN")]
    [TestCase("AUX")]
    [TestCase("NUL")]
    [TestCase("COM1")]
    [TestCase("COM9")]
    [TestCase("COM¹")]
    [TestCase("COM²")]
    [TestCase("COM³")]
    [TestCase("LPT1")]
    [TestCase("LPT9")]
    [TestCase("LPT¹")]
    [TestCase("LPT²")]
    [TestCase("LPT³")]
    [TestCase("CLOCK$")]
    public void Encode_ReservedDeviceName_AddsEncodingMarker(string pageName)
    {
        var result = new PageFileNameCodec().Encode(pageName);

        Assert.That(result, Is.EqualTo(pageName + "~mn~2~"));
    }

    /// <summary>
    /// Verifies encoded names round trip without lookalike substitutions or truncation.
    /// </summary>
    /// <param name="pageName">The page name to round trip.</param>
    [TestCase("A/B:C*D?E\"F<G>H|I")]
    [TestCase("CON")]
    [TestCase("con.notes")]
    [TestCase("Trailing. ")]
    [TestCase("100%/complete")]
    [TestCase("~mn~1~literal")]
    [TestCase("literal~mn~2~")]
    [TestCase("Literal／Slash")]
    public void EncodeDecode_PageName_RoundTrips(string pageName)
    {
        var codec = new PageFileNameCodec();

        var result = codec.Decode(codec.Encode(pageName));

        Assert.That(result, Is.EqualTo(pageName));
    }

    /// <summary>
    /// Verifies an unmarked full-width slash remains a literal page-name character.
    /// </summary>
    [Test]
    public void Decode_UnmarkedFullWidthSlash_DoesNotConvertIt()
    {
        var result = new PageFileNameCodec().Decode("Literal／Slash");

        Assert.That(result, Is.EqualTo("Literal／Slash"));
    }

    /// <summary>
    /// Verifies marker-like user files are decoded only when their encoding is canonical.
    /// </summary>
    /// <param name="fileName">The non-canonical file-name component.</param>
    [TestCase("~mn~1~100%")]
    [TestCase("~mn~1~%41")]
    [TestCase("~mn~1~%2f")]
    public void Decode_NonCanonicalMarkerName_ReturnsOriginalName(string fileName)
    {
        var result = new PageFileNameCodec().Decode(fileName);

        Assert.That(result, Is.EqualTo(fileName));
    }

    /// <summary>Verifies canonical legacy prefix names remain import-compatible.</summary>
    [TestCase("~mn~1~A%2FB", "A/B")]
    [TestCase("~mn~1~CON", "CON")]
    [TestCase("~mn~1~Trailing%2E%20", "Trailing. ")]
    public void Decode_CanonicalLegacyName_ReturnsPageName(
        string fileName,
        string expected)
    {
        var result = new PageFileNameCodec().Decode(fileName);

        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>Verifies a literal v2 marker is escaped before the real suffix marker.</summary>
    [Test]
    public void Encode_LiteralV2Suffix_EscapesMarkerTildes()
    {
        var result = new PageFileNameCodec().Encode("literal~mn~2~");

        Assert.That(result, Is.EqualTo("literal%7Emn%7E2%7E~mn~2~"));
    }
}
