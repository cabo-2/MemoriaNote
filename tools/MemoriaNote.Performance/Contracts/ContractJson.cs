using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoriaNote.Performance.Contracts;

internal static class ContractJson
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    internal static async Task<T> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(
                stream,
                Options,
                cancellationToken) ?? throw new ContractException(
                    $"'{path}' contains JSON null instead of an object.");
        }
        catch (JsonException exception)
        {
            throw new ContractException(
                $"'{path}' is not a valid {typeof(T).Name}: {exception.Message}",
                exception);
        }
    }

    internal static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
            throw new ArgumentException("The output path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    Options,
                    cancellationToken);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}

internal sealed class ContractException : Exception
{
    internal ContractException(string message)
        : base(message)
    {
    }

    internal ContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
