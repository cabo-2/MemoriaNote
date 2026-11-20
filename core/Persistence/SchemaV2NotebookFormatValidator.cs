using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;

namespace MemoriaNote.Persistence;

/// <summary>Validates Schema V2 notebooks for workspace-oriented application services.</summary>
public sealed class SchemaV2NotebookFormatValidator : INotebookFormatValidator
{
    /// <inheritdoc />
    public async Task<NotebookFormatValidation> ValidateCurrentAsync(
        string databasePath,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(databasePath);
        var recognition = await SqliteNotebookFormatRecognizer
            .RecognizeAsync(fullPath, token)
            .ConfigureAwait(false);

        switch (recognition.Classification)
        {
            case NotebookFormatClassification.Missing:
                throw new FileNotFoundException(
                    $"The notebook file does not exist: {fullPath}",
                    fullPath);
            case NotebookFormatClassification.Legacy:
                throw new InvalidDataException(
                    "The notebook uses schema v1. Run 'mn notebooks migrate' " +
                    "to create a schema v2 notebook.");
            case NotebookFormatClassification.Unsupported:
                throw new UnsupportedNotebookFormatVersionException(
                    recognition.DetectedVersion ?? "unknown");
            case NotebookFormatClassification.Corrupt:
                throw new InvalidDataException(
                    $"The file is not a valid current-format notebook: {fullPath}");
            case NotebookFormatClassification.Current:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(recognition));
        }

        var validation = await SchemaV2DatabaseValidator
            .ValidateAuthoritativeAsync(fullPath, token)
            .ConfigureAwait(false);
        if (!validation.Identity.IsValid ||
            !validation.Schema.IsValid ||
            !validation.Integrity.IsValid ||
            !validation.AuthoritativeValues.IsValid)
        {
            throw new InvalidDataException(
                $"The file is not a valid current-format notebook: {fullPath}");
        }

        var notebookId = Domain.NotebookId.FromDatabasePath(fullPath);
        var snapshot = await new SchemaV2MetadataRepository()
            .LoadAsync(notebookId, token)
            .ConfigureAwait(false);
        var readOnly = snapshot.Values.TryGetValue(
                SchemaV2MetadataContract.ReadOnlyKey,
                out var readOnlyValue) &&
            string.Equals(readOnlyValue, bool.TrueString, StringComparison.Ordinal);
        return new NotebookFormatValidation(SchemaV2Format.UserVersion, readOnly);
    }
}
