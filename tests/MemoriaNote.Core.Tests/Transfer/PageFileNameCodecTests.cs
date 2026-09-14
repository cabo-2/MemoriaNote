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
    [TestCase("A/B", "~mn~1~A%2FB")]
    [TestCase("A\\B", "~mn~1~A%5CB")]
    [TestCase("A:B", "~mn~1~A%3AB")]
    [TestCase("A*B?", "~mn~1~A%2AB%3F")]
    [TestCase("A\"B", "~mn~1~A%22B")]
    [TestCase("A<B>|", "~mn~1~A%3CB%3E%7C")]
    [TestCase("Line\nBreak", "~mn~1~Line%0ABreak")]
    [TestCase("Trailing. ", "~mn~1~Trailing%2E%20")]
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

        Assert.That(result, Is.EqualTo("~mn~1~" + pageName));
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
}
