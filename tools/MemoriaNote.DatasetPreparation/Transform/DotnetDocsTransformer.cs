using System.Text;
using MemoriaNote.DatasetPreparation.Contracts;
using MemoriaNote.DatasetPreparation.IO;

namespace MemoriaNote.DatasetPreparation.Transform;

internal sealed class DotnetDocsTransformer : IDatasetTransformer
{
    static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<DatasetInventory> InventoryAsync(
        string checkoutRoot,
        DatasetManifest manifest,
        CancellationToken token)
    {
        var contentRoot = Path.GetFullPath(manifest.Source.ContentRoot, checkoutRoot);
        EnsureDescendant(checkoutRoot, contentRoot);
        if (!Directory.Exists(contentRoot))
            throw new DirectoryNotFoundException($"Content root '{contentRoot}' was not found.");

        var sourceFiles = Directory.EnumerateFiles(contentRoot, "*.md", SearchOption.AllDirectories)
            .Select(path => new SourceFile(
                path,
                "docs/" + TreeHasher.NormalizeRelativePath(contentRoot, path)))
            .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
            .ToArray();
        var sourceTree = await TreeHasher.HashFilesAsync(
            sourceFiles.Select(file => new HashedPath(file.FullPath, file.SourcePath)),
            token);

        var pages = new List<PreparedPage>(sourceFiles.Length);
        var excluded = 0;
        foreach (var source in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            if (IsExcluded(source.SourcePath, manifest.Selection.ExcludedPathSegments))
            {
                excluded++;
                continue;
            }
            var bytes = await File.ReadAllBytesAsync(source.FullPath, token);
            var text = StrictUtf8.GetString(bytes);
            var parsed = MarkdownDocumentParser.Parse(text);
            if (string.IsNullOrWhiteSpace(parsed.Body))
            {
                excluded++;
                continue;
            }
            var relativeWithoutExtension = source.SourcePath["docs/".Length..^3];
            var title = ResolveTitle(parsed, relativeWithoutExtension, manifest.Transform.TitleFields);
            var tags = ResolveTags(parsed, relativeWithoutExtension, manifest.Transform.TagFields);
            var sourceHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(bytes))
                .ToLowerInvariant();
            var encoding = PortablePageFileNameEncoder.EncodeWithLimit(
                relativeWithoutExtension,
                sourceHash);
            var bodyBytes = StrictUtf8.GetByteCount(parsed.Body);
            pages.Add(new PreparedPage(
                source.SourcePath,
                sourceHash,
                encoding.PageName,
                encoding.FileName,
                title.Value,
                title.Source,
                tags,
                parsed.Body,
                bodyBytes,
                TreeHasher.HashText(manifest.DatasetId + "\0" + source.SourcePath)));
        }

        var ranked = StratifiedSelector.Rank(pages);
        return new DatasetInventory(sourceTree, ranked, excluded);
    }

    static bool IsExcluded(string path, IReadOnlyCollection<string> excludedSegments)
    {
        var segments = path.Split('/');
        return segments.Any(segment => excludedSegments.Contains(
            segment,
            StringComparer.OrdinalIgnoreCase));
    }

    static ResolvedTitle ResolveTitle(
        ParsedMarkdown parsed,
        string relativeWithoutExtension,
        IReadOnlyList<string> titleFields)
    {
        foreach (var field in titleFields)
        {
            if (parsed.Metadata.TryGetValue(field, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return new ResolvedTitle(value.Trim(), "front-matter:" + field);
            }
        }
        var heading = MarkdownDocumentParser.FindFirstHeading(parsed.Body);
        if (heading != null)
            return new ResolvedTitle(heading, "heading");
        return new ResolvedTitle(Path.GetFileName(relativeWithoutExtension), "path");
    }

    static IReadOnlyList<string> ResolveTags(
        ParsedMarkdown parsed,
        string relativeWithoutExtension,
        IReadOnlyList<string> tagFields)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var separator = relativeWithoutExtension.IndexOf('/');
        tags.Add(separator < 0
            ? relativeWithoutExtension
            : relativeWithoutExtension[..separator]);
        foreach (var field in tagFields)
        {
            if (parsed.Metadata.TryGetValue(field, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                tags.Add(value.Trim());
            }
        }
        return tags.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    static void EnsureDescendant(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidDataException("The content root escapes the checkout root.");
    }

    sealed record SourceFile(string FullPath, string SourcePath);

    sealed record ResolvedTitle(string Value, string Source);
}

internal sealed record DatasetInventory(
    TreeHash SourceTree,
    IReadOnlyList<PreparedPage> RankedPages,
    int ExcludedPageCount);
