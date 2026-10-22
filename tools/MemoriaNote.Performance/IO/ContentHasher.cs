using System.Security.Cryptography;
using System.Text;

namespace MemoriaNote.Performance.IO;

internal static class ContentHasher
{
    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    internal static async Task<string> HashDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(path);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Dataset directory '{root}' does not exist.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(root, file).Replace('\\', '/')
            })
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = Encoding.UTF8.GetBytes(file.RelativePath);
            hash.AppendData(pathBytes);
            hash.AppendData([0]);
            var fileHash = await HashFileAsync(file.FullPath, cancellationToken);
            hash.AppendData(Encoding.ASCII.GetBytes(fileHash));
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
