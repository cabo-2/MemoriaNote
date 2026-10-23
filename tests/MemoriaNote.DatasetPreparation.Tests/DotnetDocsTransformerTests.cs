using System.Text;
using MemoriaNote.DatasetPreparation.Preparation;
using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation.Tests;

/// <summary>Verifies dotnet/docs selection and prepared output.</summary>
[TestFixture]
public sealed class DotnetDocsTransformerTests
{
    /// <summary>Verifies fragment paths are excluded and scale output is nested and repeatable.</summary>
    [Test]
    public async Task Transform_Fixture_ProducesDeterministicNestedScales()
    {
        using var temporary = new TemporaryDirectory();
        Write(temporary.Path, "docs/core/first.md",
            "---\r\ntitle: First title\r\nms.topic: concept-article\r\n---\r\n# Ignored heading\r\nFirst body.\r\n");
        Write(temporary.Path, "docs/core/second.md", "# Second title\nSecond body.\n");
        Write(temporary.Path, "docs/includes/fragment.md", "Included fragment only.\n");
        Write(temporary.Path, "docs/samples/readme.md", "Sample only.\n");
        Write(temporary.Path, "docs/core/empty.md", "---\ntitle: Empty\n---\n");
        var manifest = ManifestFactory.Create(1, 2);
        var inventory = await new DotnetDocsTransformer().InventoryAsync(
            temporary.Path,
            manifest,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.SourceTree.FileCount, Is.EqualTo(5));
            Assert.That(inventory.RankedPages, Has.Count.EqualTo(2));
            Assert.That(inventory.ExcludedPageCount, Is.EqualTo(3));
            Assert.That(inventory.RankedPages.Select(page => page.Title),
                Is.EquivalentTo(new[] { "First title", "Second title" }));
        }

        var output = System.IO.Path.Combine(temporary.Path, "prepared");
        var writer = new PreparedDatasetWriter();
        var first = await writer.WriteAsync(output, manifest, inventory, CancellationToken.None);
        var smallNames = Directory.EnumerateFiles(
                System.IO.Path.Combine(output, "scales", "pages-1"),
                "*.txt")
            .Select(System.IO.Path.GetFileName)
            .ToArray();
        var largeNames = Directory.EnumerateFiles(
                System.IO.Path.Combine(output, "scales", "pages-2"),
                "*.txt")
            .Select(System.IO.Path.GetFileName)
            .ToArray();
        var second = await writer.WriteAsync(output, manifest, inventory, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(smallNames, Has.Length.EqualTo(1));
            Assert.That(largeNames, Does.Contain(smallNames[0]));
            Assert.That(first.PreparedTree.Sha256, Is.EqualTo(second.PreparedTree.Sha256));
            Assert.That(first.Statistics.FrontMatterTitleCount, Is.EqualTo(1));
            Assert.That(first.Statistics.HeadingTitleCount, Is.EqualTo(1));
        }
    }

    static void Write(string root, string relativePath, string content)
    {
        var path = System.IO.Path.Combine(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
