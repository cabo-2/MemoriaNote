using System.Security.Cryptography;
using System.Text;
using MemoriaNote.DatasetPreparation.Contracts;
using MemoriaNote.DatasetPreparation.IO;

namespace MemoriaNote.DatasetPreparation.Transform;

internal sealed class MdnTranslatedContentTransformer : IDatasetTransformer
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
                TreeHasher.NormalizeRelativePath(checkoutRoot, path),
                TreeHasher.NormalizeRelativePath(contentRoot, path)))
            .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
            .ToArray();
        var sourceTree = await TreeHasher.HashFilesAsync(
            sourceFiles.Select(file => new HashedPath(file.FullPath, file.SourcePath)),
            token);

        var pages = new List<PreparedPage>(sourceFiles.Length);
        var pageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = 0;
        foreach (var source in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            if (IsExcluded(source.RelativePath, manifest.Selection.ExcludedPathSegments))
            {
                excluded++;
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(source.FullPath, token);
            var text = StrictUtf8.GetString(bytes);
            var parsed = MarkdownDocumentParser.Parse(text);
            if (string.IsNullOrWhiteSpace(parsed.Body) ||
                !TryResolvePageName(parsed, out var pageName))
            {
                excluded++;
                continue;
            }
            if (!pageNames.Add(pageName))
            {
                throw new InvalidDataException(
                    $"MDN page name '{pageName}' is duplicated by '{source.SourcePath}'.");
            }

            var relativeWithoutExtension = source.RelativePath[..^3];
            var title = ResolveTitle(
                parsed,
                relativeWithoutExtension,
                manifest.Transform.TitleFields);
            var tags = ResolveTags(parsed, manifest.Transform.TagFields);
            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var encoding = PortablePageFileNameEncoder.EncodeWithLimit(pageName, sourceHash);
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

        return new DatasetInventory(
            sourceTree,
            StratifiedSelector.Rank(pages),
            excluded);
    }

    static bool IsExcluded(string path, IReadOnlyCollection<string> excludedSegments)
    {
        var segments = path.Split('/');
        return segments.Any(segment => excludedSegments.Contains(
            segment,
            StringComparer.OrdinalIgnoreCase));
    }

    static bool TryResolvePageName(ParsedMarkdown parsed, out string pageName)
    {
        pageName = string.Empty;
        if (!parsed.Metadata.TryGetValue("slug", out var slug))
            return false;
        var normalized = slug.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }
        pageName = normalized;
        return true;
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
        IReadOnlyList<string> tagFields)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in tagFields)
        {
            if (!parsed.Metadata.TryGetValue(field, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            var segments = value.Trim().Replace('\\', '/').Trim('/').Split('/');
            if (segments.Length > 0 && segments[0].Length > 0)
                tags.Add(segments[0].ToLowerInvariant());
            if (segments.Length > 1 && segments[1].Length > 0)
            {
                tags.Add(
                    (segments[0] + "/" + segments[1]).ToLowerInvariant());
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

    sealed record SourceFile(string FullPath, string SourcePath, string RelativePath);

    sealed record ResolvedTitle(string Value, string Source);
}
