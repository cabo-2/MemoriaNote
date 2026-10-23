using System.Security.Cryptography;
using System.Text;

namespace MemoriaNote.DatasetPreparation.IO;

internal static class TreeHasher
{
    internal static async Task<TreeHash> HashDirectoryAsync(
        string root,
        CancellationToken token)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Directory '{root}' was not found.");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new HashedPath(path, NormalizeRelativePath(root, path)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return await HashFilesAsync(files, token);
    }

    internal static async Task<TreeHash> HashFilesAsync(
        IEnumerable<HashedPath> files,
        CancellationToken token)
    {
        using var treeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        long bytes = 0;
        foreach (var file in files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(file.FullPath);
            var fileHash = await HashFileAsync(file.FullPath, token);
            Append(treeHash, file.RelativePath);
            treeHash.AppendData([0]);
            Append(treeHash, fileHash);
            treeHash.AppendData([(byte)'\n']);
            count++;
            bytes += info.Length;
        }
        return new TreeHash(
            Convert.ToHexString(treeHash.GetHashAndReset()).ToLowerInvariant(),
            count,
            bytes);
    }

    internal static async Task<string> HashFileAsync(
        string path,
        CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string HashText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    internal static string NormalizeRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
    }
}

internal sealed record HashedPath(string FullPath, string RelativePath);

internal sealed record TreeHash(string Sha256, int FileCount, long TotalBytes);
