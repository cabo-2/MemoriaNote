using System.Text;
using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation.Tests;

/// <summary>Verifies prepared file names remain compatible with the v0.2.0 importer.</summary>
[TestFixture]
public sealed class PortablePageFileNameEncoderTests
{
    /// <summary>Verifies path separators use the published page-name encoding.</summary>
    [Test]
    public void Encode_PathName_UsesVersionTwoCodec()
    {
        Assert.That(
            PortablePageFileNameEncoder.Encode("core/introduction"),
            Is.EqualTo("core%2Fintroduction~mn~2~"));
    }

    /// <summary>Verifies long source paths are shortened deterministically and portably.</summary>
    [Test]
    public void EncodeWithLimit_LongName_AppendsSourceHashWithinLimit()
    {
        var hash = new string('a', 64);
        var first = PortablePageFileNameEncoder.EncodeWithLimit(new string('x', 400), hash);
        var second = PortablePageFileNameEncoder.EncodeWithLimit(new string('x', 400), hash);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.WasShortened, Is.True);
            Assert.That(first.PageName, Does.EndWith(" — aaaaaaaaaaaa"));
            Assert.That(Encoding.UTF8.GetByteCount(first.FileName), Is.LessThanOrEqualTo(255));
        }
    }
}
