using System.Text;
using System.Text.Json;
using MemoriaNote.DatasetPreparation.Contracts;
using MemoriaNote.DatasetPreparation.IO;
using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation.Preparation;

internal sealed class PreparedDatasetWriter
{
    static readonly UTF8Encoding Utf8NoBom = new(false);
    static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal async Task<PreparationResult> WriteAsync(
        string outputDirectory,
        DatasetManifest manifest,
        DatasetInventory inventory,
        CancellationToken token)
    {
        var maximumPageCount = manifest.Selection.Scales.Max(scale => scale.PageCount);
        if (inventory.RankedPages.Count < maximumPageCount)
        {
            throw new InvalidDataException(
                $"The dataset has {inventory.RankedPages.Count} eligible pages but " +
                $"scale '{maximumPageCount}' was requested.");
        }
        var selected = inventory.RankedPages.Take(maximumPageCount).ToArray();
        var parent = Path.GetDirectoryName(Path.GetFullPath(outputDirectory)) ??
            throw new InvalidDataException("The prepared output must have a parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".prepared-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await WriteCatalogAsync(staging, selected, token);
            foreach (var scale in manifest.Selection.Scales)
                await WriteScaleAsync(staging, scale, selected, token);

            var statistics = CreateStatistics(inventory, selected);
            await WriteJsonAsync(
                Path.Combine(staging, "statistics.json"),
                statistics,
                token);
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
            Directory.Move(staging, outputDirectory);

            var preparedTree = await TreeHasher.HashDirectoryAsync(outputDirectory, token);
            return new PreparationResult(preparedTree, statistics);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    static async Task WriteCatalogAsync(
        string root,
        IReadOnlyList<PreparedPage> pages,
        CancellationToken token)
    {
        await using var stream = new FileStream(
            Path.Combine(root, "catalog.ndjson"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: false)
        {
            NewLine = "\n"
        };
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var record = new
            {
                rank = index,
                page.SourcePath,
                page.SourceSha256,
                page.Name,
                page.FileName,
                page.Title,
                page.TitleSource,
                page.Tags,
                page.BodyBytes
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(record, CompactJson));
            token.ThrowIfCancellationRequested();
        }
    }

    static async Task WriteScaleAsync(
        string root,
        DatasetScale scale,
        IReadOnlyList<PreparedPage> pages,
        CancellationToken token)
    {
        var directory = Path.Combine(root, "scales", scale.ScaleId);
        Directory.CreateDirectory(directory);
        foreach (var page in pages.Take(scale.PageCount))
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, page.FileName),
                page.Body,
                Utf8NoBom,
                token);
        }
    }

    static DatasetStatistics CreateStatistics(
        DatasetInventory inventory,
        IReadOnlyCollection<PreparedPage> selected)
    {
        return new DatasetStatistics
        {
            SourceMarkdownCount = inventory.SourceTree.FileCount,
            SourceMarkdownBytes = inventory.SourceTree.TotalBytes,
            EligiblePageCount = inventory.RankedPages.Count,
            ExcludedPageCount = inventory.ExcludedPageCount,
            SelectedPageCount = selected.Count,
            SelectedBodyBytes = selected.Sum(page => (long)page.BodyBytes),
            FrontMatterTitleCount = selected.Count(page =>
                page.TitleSource.StartsWith("front-matter:", StringComparison.Ordinal)),
            HeadingTitleCount = selected.Count(page => page.TitleSource == "heading"),
            PathTitleCount = selected.Count(page => page.TitleSource == "path")
        };
    }

    static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken token)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, ManifestJson.Options, token);
        await stream.WriteAsync("\n"u8.ToArray(), token);
    }
}

internal sealed record PreparationResult(
    TreeHash PreparedTree,
    DatasetStatistics Statistics);
