using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Transfer;

/// <summary>
/// Verifies portable text-transfer path conversion.
/// </summary>
[TestFixture]
public sealed class TextTransferPathCodecTests
{
    readonly TextTransferPathCodec _codec =
        new(new PageFileNameCodec());

    /// <summary>
    /// Verifies every relative path component is encoded and decoded independently.
    /// </summary>
    [Test]
    public void EncodeDecode_UnsafeComponents_RoundTripsPortablePath()
    {
        const string portablePath = "CON/design:2026/Literal／Slash";

        var systemPath = _codec.EncodeRelativePath(portablePath);
        var result = _codec.DecodeRelativePath(systemPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                systemPath,
                Is.EqualTo(Path.Combine(
                    "~mn~1~CON",
                    "~mn~1~design%3A2026",
                    "Literal／Slash")));
            Assert.That(result, Is.EqualTo(portablePath));
        }
    }

    /// <summary>
    /// Verifies a drive-like component is encoded when it is not an absolute path.
    /// </summary>
    [Test]
    public void EncodeDecode_DriveLikeComponent_RoundTripsPortablePath()
    {
        const string portablePath = "C:X/archive";

        var systemPath = _codec.EncodeRelativePath(portablePath);
        var result = _codec.DecodeRelativePath(systemPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                systemPath,
                Is.EqualTo(Path.Combine("~mn~1~C%3AX", "archive")));
            Assert.That(result, Is.EqualTo(portablePath));
        }
    }

    /// <summary>
    /// Verifies null and empty root paths retain their existing meanings.
    /// </summary>
    [Test]
    public void PathConversion_RootPath_RemainsEmpty()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_codec.EncodeRelativePath(null!), Is.Null);
            Assert.That(_codec.DecodeRelativePath(null!), Is.Null);
            Assert.That(_codec.EncodeRelativePath(string.Empty), Is.Empty);
            Assert.That(_codec.DecodeRelativePath(string.Empty), Is.Empty);
        }
    }

    /// <summary>
    /// Verifies absolute and traversal paths cannot escape the export root.
    /// </summary>
    /// <param name="relativePath">The unsafe portable path.</param>
    [TestCase("/absolute")]
    [TestCase("\\absolute")]
    [TestCase("C:/absolute")]
    [TestCase("../outside")]
    [TestCase("inside/../outside")]
    [TestCase("inside//outside")]
    public void EncodeRelativePath_UnsafePath_ThrowsArgumentException(
        string relativePath)
    {
        Action encode = () =>
        {
            _codec.EncodeRelativePath(relativePath);
        };

        Assert.Throws<ArgumentException>(encode);
    }
}
