using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoriaNote.DatasetPreparation.Contracts;

internal static class ManifestJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    internal static async Task<DatasetManifest> ReadAsync(
        string path,
        CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DatasetManifest>(
                stream,
                Options,
                token)
            ?? throw new InvalidDataException("The dataset manifest is empty.");
    }

    internal static async Task WriteAsync(
        string path,
        DatasetManifest manifest,
        CancellationToken token)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, Options, token);
                await stream.WriteAsync("\n"u8.ToArray(), token);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
