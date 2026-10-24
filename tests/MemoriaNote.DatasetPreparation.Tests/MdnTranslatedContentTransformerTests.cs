using System.Text;
using MemoriaNote.DatasetPreparation.Preparation;
using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation.Tests;

/// <summary>Verifies mdn/translated-content Japanese selection and transformation.</summary>
[TestFixture]
public sealed class MdnTranslatedContentTransformerTests
{
    /// <summary>Verifies MDN metadata mapping, exclusions, and nested scale output.</summary>
    [Test]
    public async Task Transform_Fixture_MapsSlugTitleAndCategoriesDeterministically()
    {
        using var temporary = new TemporaryDirectory();
        Write(temporary.Path, "files/ja/web/api/first/index.md",
            "---\r\ntitle: 最初のページ\r\nslug: Web/API/First\r\nl10n:\r\n  sourceCommit: abcdef\r\n---\r\n最初の本文です。\r\n");
        Write(temporary.Path, "files/ja/glossary/second/index.md",
            "---\ntitle: 第二ページ\nslug: Glossary/Second\n---\n# 見出し\n第二の本文です。\n");
        Write(temporary.Path, "files/ja/orphaned/old/index.md",
            "---\ntitle: 旧ページ\nslug: Orphaned/Old\n---\n旧本文です。\n");
        Write(temporary.Path, "files/ja/web/empty/index.md",
            "---\ntitle: 空ページ\nslug: Web/Empty\n---\n");
        Write(temporary.Path, "files/ja/web/missing-slug/index.md",
            "---\ntitle: Slugなし\n---\n本文です。\n");
        var manifest = ManifestFactory.CreateMdn(1, 2);
        var transformer = new MdnTranslatedContentTransformer();

        var firstInventory = await transformer.InventoryAsync(
            temporary.Path,
            manifest,
            CancellationToken.None);
        var secondInventory = await transformer.InventoryAsync(
            temporary.Path,
            manifest,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstInventory.SourceTree.FileCount, Is.EqualTo(5));
            Assert.That(firstInventory.RankedPages, Has.Count.EqualTo(2));
            Assert.That(firstInventory.ExcludedPageCount, Is.EqualTo(3));
            Assert.That(
                firstInventory.RankedPages.Select(page => page.Name),
                Is.EquivalentTo(new[] { "Web/API/First", "Glossary/Second" }));
            Assert.That(
                firstInventory.RankedPages.Select(page => page.Title),
                Is.EquivalentTo(new[] { "最初のページ", "第二ページ" }));
            Assert.That(
                firstInventory.RankedPages.Single(page => page.Name == "Web/API/First").Tags,
                Is.EqualTo(new[] { "web", "web/api" }));
            Assert.That(
                firstInventory.RankedPages.Select(page => page.SourcePath),
                Has.All.StartsWith("files/ja/"));
            Assert.That(
                firstInventory.RankedPages.Select(page => page.Body),
                Has.None.Contains("slug:"));
            Assert.That(
                firstInventory.RankedPages.Select(page => page.SourcePath),
                Is.EqualTo(secondInventory.RankedPages.Select(page => page.SourcePath)));
        }

        var output = System.IO.Path.Combine(temporary.Path, "prepared");
        var first = await new PreparedDatasetWriter().WriteAsync(
            output,
            manifest,
            firstInventory,
            CancellationToken.None);
        var second = await new PreparedDatasetWriter().WriteAsync(
            output,
            manifest,
            secondInventory,
            CancellationToken.None);

        Assert.That(first.PreparedTree.Sha256, Is.EqualTo(second.PreparedTree.Sha256));
    }

    /// <summary>Verifies ambiguous MDN slugs fail instead of overwriting a prepared page.</summary>
    [Test]
    public void Transform_DuplicateSlug_IsRejected()
    {
        using var temporary = new TemporaryDirectory();
        Write(temporary.Path, "files/ja/web/first/index.md",
            "---\ntitle: First\nslug: Web/Duplicate\n---\n本文です。\n");
        Write(temporary.Path, "files/ja/web/second/index.md",
            "---\ntitle: Second\nslug: web/duplicate\n---\n別の本文です。\n");
        var manifest = ManifestFactory.CreateMdn(1);

        Assert.That(
            async () => await new MdnTranslatedContentTransformer().InventoryAsync(
                temporary.Path,
                manifest,
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
    }

    static void Write(string root, string relativePath, string content)
    {
        var path = System.IO.Path.Combine(
            root,
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
