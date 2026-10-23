using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation.Tests;

/// <summary>Verifies the bounded Markdown and front-matter parsing contract.</summary>
[TestFixture]
public sealed class MarkdownDocumentParserTests
{
    /// <summary>Verifies scalar metadata is read and front matter is removed.</summary>
    [Test]
    public void Parse_FrontMatterAndCrLf_NormalizesBody()
    {
        var parsed = MarkdownDocumentParser.Parse(
            "\uFEFF---\r\ntitle: \"A .NET title\"\r\nms.topic: concept-article # fixed\r\n---\r\n# Heading\r\nBody\r\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Metadata["title"], Is.EqualTo("A .NET title"));
            Assert.That(parsed.Metadata["ms.topic"], Is.EqualTo("concept-article"));
            Assert.That(parsed.Body, Is.EqualTo("# Heading\nBody\n"));
        }
    }

    /// <summary>Verifies unsupported YAML collections are ignored rather than guessed.</summary>
    [Test]
    public void Parse_CollectionValue_DoesNotInventScalarMetadata()
    {
        var parsed = MarkdownDocumentParser.Parse(
            "---\ntitle: [one, two]\n---\n# Reliable heading\n");

        Assert.That(parsed.Metadata, Does.Not.ContainKey("title"));
        Assert.That(
            MarkdownDocumentParser.FindFirstHeading(parsed.Body),
            Is.EqualTo("Reliable heading"));
    }
}
