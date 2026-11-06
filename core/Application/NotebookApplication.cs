#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Domain;
using MemoriaNote.Persistence;
using MemoriaNote.Persistence.SchemaV2;
using MemoriaNote.Persistence.SchemaV2.Validation;

namespace MemoriaNote.Application;

/// <summary>Creates and opens notebooks through the maintained application surface.</summary>
public static class NotebookApplication
{
    /// <summary>Creates and publishes a canonical current-format notebook.</summary>
    /// <param name="request">The notebook creation request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A validated application session or a classified failure.</returns>
    public static async Task<NotebookOperationResult<NotebookApplicationSession>> CreateAsync(
        NotebookCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await SchemaV2DatabaseFactory.CreateAsync(
                    request.DatabasePath,
                    request.Title,
                    cancellationToken)
                .ConfigureAwait(false);
            return NotebookOperationResult<NotebookApplicationSession>.Succeeded(
                CreateSession(request.DatabasePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException) when (DestinationConflicts(request.DatabasePath))
        {
            return NotebookOperationResult<NotebookApplicationSession>.Failed(
                NotebookFailureFactory.Conflict(
                    "notebook.destination_exists",
                    "The notebook destination or one of its SQLite sidecars already exists."));
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            return NotebookOperationResult<NotebookApplicationSession>.Failed(
                NotebookFailureFactory.FromException(exception, "notebook.create"));
        }
    }

    /// <summary>Recognizes and validates an existing current-format notebook.</summary>
    /// <param name="request">The notebook open request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A validated application session or a classified failure.</returns>
    public static async Task<NotebookOperationResult<NotebookApplicationSession>> OpenAsync(
        NotebookOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var recognition = await SqliteNotebookFormatRecognizer
                .RecognizeAsync(request.DatabasePath, cancellationToken)
                .ConfigureAwait(false);
            var recognitionFailure = FailureFor(recognition);
            if (recognitionFailure is not null)
            {
                return NotebookOperationResult<NotebookApplicationSession>.Failed(
                    recognitionFailure);
            }

            var validation = await SchemaV2DatabaseValidator
                .ValidateAuthoritativeAsync(request.DatabasePath, cancellationToken)
                .ConfigureAwait(false);
            var validationFailure = FailureFor(validation);
            if (validationFailure is not null)
            {
                return NotebookOperationResult<NotebookApplicationSession>.Failed(
                    validationFailure);
            }

            return NotebookOperationResult<NotebookApplicationSession>.Succeeded(
                CreateSession(request.DatabasePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (NotebookFailureFactory.IsClassifiable(exception))
        {
            return NotebookOperationResult<NotebookApplicationSession>.Failed(
                NotebookFailureFactory.FromException(exception, "notebook.open"));
        }
    }

    private static NotebookApplicationSession CreateSession(string databasePath)
    {
        var notebookId = NotebookId.FromDatabasePath(databasePath);
        var provider = new SchemaV2RepositoryProvider(SystemClock.Instance);
        return new NotebookApplicationSession(
            new NotebookDescriptor(notebookId, SchemaV2Format.UserVersion),
            provider);
    }

    private static NotebookFailure? FailureFor(NotebookFormatRecognition recognition)
    {
        return recognition.Classification switch
        {
            NotebookFormatClassification.Current => null,
            NotebookFormatClassification.Missing => NotebookFailureFactory.NotFound(
                "notebook.missing",
                "The notebook does not exist."),
            NotebookFormatClassification.Legacy => NotebookFailureFactory.Format(
                "format.legacy",
                "The notebook uses a legacy format and must be migrated before it can be opened."),
            NotebookFormatClassification.Unsupported => NotebookFailureFactory.Format(
                "format.unsupported",
                "The notebook uses an unsupported format version."),
            NotebookFormatClassification.Corrupt => NotebookFailureFactory.Format(
                "format.corrupt",
                "The notebook format identity is corrupt or unrecognized."),
            _ => throw new ArgumentOutOfRangeException(nameof(recognition))
        };
    }

    private static NotebookFailure? FailureFor(SchemaV2DatabaseValidation validation)
    {
        if (!validation.Identity.IsValid)
        {
            return NotebookFailureFactory.Format(
                "format.identity_invalid",
                "The notebook format identity failed validation.");
        }
        if (!validation.Schema.IsValid)
        {
            return NotebookFailureFactory.Format(
                "format.schema_invalid",
                "The notebook schema failed validation.");
        }
        if (!validation.Integrity.IsValid)
        {
            return NotebookFailureFactory.Format(
                "format.integrity_invalid",
                "The notebook failed SQLite integrity validation.");
        }
        if (!validation.AuthoritativeValues.IsValid)
        {
            return NotebookFailureFactory.Format(
                "format.authoritative_values_invalid",
                "The notebook contains invalid authoritative values.");
        }

        return null;
    }

    private static bool DestinationConflicts(string databasePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(databasePath);
            if (Directory.Exists(fullPath))
                return true;

            return SqliteDatabaseFiles.Enumerate(fullPath).Any(path =>
            {
                var information = new FileInfo(path);
                information.Refresh();
                return information.Exists || information.LinkTarget is not null;
            });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }
}
