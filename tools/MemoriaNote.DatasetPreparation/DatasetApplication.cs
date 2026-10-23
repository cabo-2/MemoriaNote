using System.Text.Json;
using MemoriaNote.DatasetPreparation.Acquisition;
using MemoriaNote.DatasetPreparation.CommandLine;
using MemoriaNote.DatasetPreparation.Contracts;
using MemoriaNote.DatasetPreparation.IO;
using MemoriaNote.DatasetPreparation.Preparation;
using MemoriaNote.DatasetPreparation.Transform;

namespace MemoriaNote.DatasetPreparation;

internal sealed class DatasetApplication
{
    readonly TextWriter _output;
    readonly GitSourceManager _gitSourceManager = new();
    readonly DotnetDocsTransformer _transformer = new();
    readonly PreparedDatasetWriter _writer = new();

    internal DatasetApplication(TextWriter output)
    {
        _output = output;
    }

    internal async Task<int> RunAsync(string[] arguments, CancellationToken token)
    {
        try
        {
            var command = CommandLineParser.Parse(arguments);
            switch (command)
            {
                case HelpCommand:
                    await _output.WriteLineAsync(HelpText);
                    return 0;
                case VerifyCommand verify:
                    await VerifyAsync(verify.Options, token);
                    return 0;
                case InventoryCommand inventory:
                    await InventoryAsync(inventory.Options, token);
                    return 0;
                case PrepareCommand prepare:
                    await PrepareAsync(prepare.Options, token);
                    return 0;
                default:
                    throw new InvalidOperationException("Unsupported dataset command.");
            }
        }
        catch (CommandLineException exception)
        {
            await _output.WriteLineAsync("Usage error: " + exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _output.WriteLineAsync("Dataset preparation failed: " + exception.Message);
            return 1;
        }
    }

    async Task InventoryAsync(DatasetOptions options, CancellationToken token)
    {
        var (manifest, manifestPath) = await ReadManifestAsync(options, allowUnlocked: false, token);
        var sourceRoot = await ResolveSourceRootAsync(options, manifest, token);
        var inventory = await _transformer.InventoryAsync(sourceRoot, manifest, token);
        var maximum = manifest.Selection.Scales.Max(scale => scale.PageCount);
        var selected = inventory.RankedPages.Take(Math.Min(maximum, inventory.RankedPages.Count));
        var report = new
        {
            manifest = Path.GetFileName(manifestPath),
            manifest.DatasetId,
            manifest.DatasetVersion,
            sourceRevision = manifest.Source.Revision,
            sourceContentSha256 = inventory.SourceTree.Sha256,
            sourceMarkdownCount = inventory.SourceTree.FileCount,
            sourceMarkdownBytes = inventory.SourceTree.TotalBytes,
            eligiblePageCount = inventory.RankedPages.Count,
            excludedPageCount = inventory.ExcludedPageCount,
            requestedMaximumPageCount = maximum,
            selectedBodyBytes = selected.Sum(page => (long)page.BodyBytes)
        };
        await _output.WriteLineAsync(JsonSerializer.Serialize(report, ManifestJson.Options));
    }

    async Task PrepareAsync(DatasetOptions options, CancellationToken token)
    {
        var (manifest, manifestPath) = await ReadManifestAsync(
            options,
            allowUnlocked: options.UpdateLock,
            token);
        var sourceRoot = await ResolveSourceRootAsync(options, manifest, token);
        var inventory = await _transformer.InventoryAsync(sourceRoot, manifest, token);
        if (!options.UpdateLock && inventory.SourceTree.Sha256 != manifest.Source.ContentSha256)
        {
            throw new InvalidDataException(
                $"Source content checksum mismatch: expected {manifest.Source.ContentSha256}, " +
                $"actual {inventory.SourceTree.Sha256}.");
        }
        var datasetRoot = ResolveDatasetRoot(options, manifest);
        var result = await _writer.WriteAsync(datasetRoot, manifest, inventory, token);
        if (options.UpdateLock)
        {
            manifest.Source.ContentSha256 = inventory.SourceTree.Sha256;
            manifest.Prepared.Sha256 = result.PreparedTree.Sha256;
            manifest.Prepared.PageCount = manifest.Selection.Scales.Max(scale => scale.PageCount);
            manifest.Prepared.TotalBytes = result.PreparedTree.TotalBytes;
            manifest.Statistics = result.Statistics;
            ManifestValidator.Validate(manifest, allowUnlocked: false);
            await ManifestJson.WriteAsync(manifestPath, manifest, token);
        }
        else
        {
            ValidatePreparedLock(manifest, result);
        }
        await _output.WriteLineAsync(
            $"Prepared {manifest.Prepared.PageCount} pages for {manifest.DatasetId} " +
            $"({result.PreparedTree.Sha256}).");
    }

    async Task VerifyAsync(DatasetOptions options, CancellationToken token)
    {
        var (manifest, _) = await ReadManifestAsync(options, allowUnlocked: false, token);
        var datasetRoot = ResolveDatasetRoot(options, manifest);
        var actual = await TreeHasher.HashDirectoryAsync(datasetRoot, token);
        if (actual.Sha256 != manifest.Prepared.Sha256)
        {
            throw new InvalidDataException(
                $"Prepared checksum mismatch: expected {manifest.Prepared.Sha256}, " +
                $"actual {actual.Sha256}.");
        }
        if (actual.TotalBytes != manifest.Prepared.TotalBytes)
        {
            throw new InvalidDataException(
                $"Prepared byte count mismatch: expected {manifest.Prepared.TotalBytes}, " +
                $"actual {actual.TotalBytes}.");
        }
        foreach (var scale in manifest.Selection.Scales)
        {
            var scalePath = Path.Combine(datasetRoot, "scales", scale.ScaleId);
            var count = Directory.Exists(scalePath)
                ? Directory.EnumerateFiles(scalePath, "*.txt", SearchOption.TopDirectoryOnly).Count()
                : 0;
            if (count != scale.PageCount)
            {
                throw new InvalidDataException(
                    $"Scale '{scale.ScaleId}' contains {count} pages; expected {scale.PageCount}.");
            }
        }
        await _output.WriteLineAsync(
            $"Verified {manifest.DatasetId} ({actual.Sha256}).");
    }

    async Task<(DatasetManifest Manifest, string ManifestPath)> ReadManifestAsync(
        DatasetOptions options,
        bool allowUnlocked,
        CancellationToken token)
    {
        var manifestPath = Path.GetFullPath(options.ManifestPath);
        var manifest = await ManifestJson.ReadAsync(manifestPath, token);
        ManifestValidator.Validate(manifest, allowUnlocked);
        var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
        foreach (var license in manifest.Licenses)
        {
            var licensePath = Path.GetFullPath(license.LicenseFile, manifestDirectory);
            if (!File.Exists(licensePath))
                throw new FileNotFoundException("Dataset license record was not found.", licensePath);
        }
        return (manifest, manifestPath);
    }

    async Task<string> ResolveSourceRootAsync(
        DatasetOptions options,
        DatasetManifest manifest,
        CancellationToken token)
    {
        if (options.SourceRoot != null)
        {
            var sourceRoot = Path.GetFullPath(options.SourceRoot);
            await _gitSourceManager.VerifyRevisionAsync(
                sourceRoot,
                manifest.Source.Revision,
                token);
            return sourceRoot;
        }
        var path = Path.Combine(
            Path.GetFullPath(options.CacheRoot),
            "sources",
            manifest.DatasetId,
            manifest.Source.Revision);
        return await _gitSourceManager.AcquireAsync(manifest.Source, path, token);
    }

    static string ResolveDatasetRoot(DatasetOptions options, DatasetManifest manifest)
    {
        return options.DatasetRoot != null
            ? Path.GetFullPath(options.DatasetRoot)
            : Path.Combine(
                Path.GetFullPath(options.CacheRoot),
                "datasets",
                manifest.DatasetId,
                manifest.DatasetVersion,
                manifest.Prepared.RelativePath);
    }

    static void ValidatePreparedLock(DatasetManifest manifest, PreparationResult result)
    {
        var issues = new List<string>();
        if (result.PreparedTree.Sha256 != manifest.Prepared.Sha256)
            issues.Add("prepared SHA-256");
        if (result.PreparedTree.TotalBytes != manifest.Prepared.TotalBytes)
            issues.Add("prepared total bytes");
        var actualStatistics = JsonSerializer.Serialize(result.Statistics, ManifestJson.Options);
        var expectedStatistics = JsonSerializer.Serialize(manifest.Statistics, ManifestJson.Options);
        if (actualStatistics != expectedStatistics)
            issues.Add("dataset statistics");
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "Prepared output does not match the manifest lock: " + string.Join(", ", issues) + ".");
        }
    }

    const string HelpText =
        "MemoriaNote dataset preparation\n\n" +
        "Commands:\n" +
        "  inventory --manifest <path> --cache-root <path> [--source-root <path>]\n" +
        "  prepare   --manifest <path> --cache-root <path> [--source-root <path>]\n" +
        "            [--dataset-root <path>] [--update-lock]\n" +
        "  verify    --manifest <path> --cache-root <path> [--dataset-root <path>]\n\n" +
        "prepare may access the network only when the pinned Git source is absent.\n" +
        "verify never downloads or transforms a dataset.";
}
