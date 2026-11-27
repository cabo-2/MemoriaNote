#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence.SchemaV2.Validation;
using Microsoft.Data.Sqlite;

namespace MemoriaNote.Persistence.SchemaV2;

/// <summary>Owns a repairability preflight and authoritative records in one read snapshot.</summary>
internal sealed class SchemaV2RepairSnapshot : IAsyncDisposable
{
    private const int BusyTimeoutSeconds = 30;
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;

    private SchemaV2RepairSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata,
        NotebookLogicalFingerprint fingerprint)
    {
        _connection = connection;
        _transaction = transaction;
        Metadata = metadata;
        Fingerprint = fingerprint;
    }

    internal IReadOnlyList<CurrentNotebookMetadataRecord> Metadata { get; }

    internal NotebookLogicalFingerprint Fingerprint { get; }

    internal CurrentNotebookTransfer CreateTransfer(CancellationToken cancellationToken) =>
        new(Metadata, ReadPagesAsync(cancellationToken));

    internal static async Task<SchemaV2RepairSnapshotResult> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        var contract = await SchemaV2ExpectedContract.LoadEmbeddedAsync(cancellationToken)
            .ConfigureAwait(false);
        var connection = await SqliteConnectionFactory.OpenAsync(
                databasePath,
                SqliteOpenMode.ReadOnly,
                cancellationToken)
            .ConfigureAwait(false);
        connection.DefaultTimeout = BusyTimeoutSeconds;
        try
        {
            var transaction = connection.BeginTransaction(
                IsolationLevel.Serializable,
                deferred: true);
            try
            {
                var observation = await DatabaseSchemaInspector
                    .InspectAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                if (!SchemaV2Format.IsCurrentIdentity(
                        observation.ApplicationId,
                        observation.UserVersion))
                {
                    throw new NotebookRecognitionConcurrentChangeException();
                }

                var schemaIssues = SchemaV2RepairabilityValidator
                    .ValidateAuthoritativeSchema(contract, observation);
                if (schemaIssues.Count > 0)
                {
                    await DisposeAsync(connection, transaction).ConfigureAwait(false);
                    return SchemaV2RepairSnapshotResult.Refused(
                        SchemaV2RepairabilityIssueKind.AuthoritativeSchema,
                        schemaIssues.Count);
                }

                var integrityIssues = await SchemaV2RepairabilityValidator
                    .ValidateAuthoritativeIntegrityAsync(
                        connection,
                        transaction,
                        contract.AuthoritativeTables,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (integrityIssues.Count > 0)
                {
                    await DisposeAsync(connection, transaction).ConfigureAwait(false);
                    return SchemaV2RepairSnapshotResult.Refused(
                        SchemaV2RepairabilityIssueKind.AuthoritativeIntegrity,
                        integrityIssues.Count);
                }

                var valueIssues = await SchemaV2DatabaseValidator
                    .ValidateAuthoritativeValuesAsync(
                        connection,
                        transaction,
                        contract,
                        observation,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (valueIssues.Count > 0)
                {
                    await DisposeAsync(connection, transaction).ConfigureAwait(false);
                    return SchemaV2RepairSnapshotResult.Refused(
                        SchemaV2RepairabilityIssueKind.AuthoritativeValues,
                        valueIssues.Count);
                }

                var metadata = new List<CurrentNotebookMetadataRecord>();
                var fingerprint = new NotebookLogicalFingerprintBuilder();
                foreach (var row in observation.Metadata)
                {
                    var record = new CurrentNotebookMetadataRecord(row.Key, row.Value);
                    metadata.Add(record);
                    fingerprint.Add(record);
                }

                await AddPageFingerprintAsync(
                        connection,
                        transaction,
                        fingerprint,
                        cancellationToken)
                    .ConfigureAwait(false);
                return SchemaV2RepairSnapshotResult.Repairable(
                    new SchemaV2RepairSnapshot(
                        connection,
                        transaction,
                        metadata.AsReadOnly(),
                        fingerprint.Build()));
            }
            catch
            {
                await DisposeAsync(connection, transaction).ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async IAsyncEnumerable<CurrentNotebookPageRecord> ReadPagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = CreatePageCommand(_connection, _transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            yield return ReadPage(reader);
    }

    private static async Task AddPageFingerprintAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NotebookLogicalFingerprintBuilder fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = CreatePageCommand(connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            fingerprint.Add(ReadPage(reader));
    }

    private static SqliteCommand CreatePageCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT PageId, Name, CreateTime, UpdateTime, Revision, Tags, Text
            FROM Pages
            ORDER BY InternalId;
            """;
        return command;
    }

    private static CurrentNotebookPageRecord ReadPage(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetString(6));

    public async ValueTask DisposeAsync() =>
        await DisposeAsync(_connection, _transaction).ConfigureAwait(false);

    private static async ValueTask DisposeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        await transaction.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}

internal enum SchemaV2RepairabilityIssueKind
{
    AuthoritativeSchema,
    AuthoritativeIntegrity,
    AuthoritativeValues
}

internal sealed record SchemaV2RepairSnapshotResult(
    SchemaV2RepairSnapshot? Snapshot,
    SchemaV2RepairabilityIssueKind? IssueKind,
    int IssueCount)
{
    internal bool IsRepairable => Snapshot is not null;

    internal static SchemaV2RepairSnapshotResult Repairable(
        SchemaV2RepairSnapshot snapshot) => new(snapshot, null, 0);

    internal static SchemaV2RepairSnapshotResult Refused(
        SchemaV2RepairabilityIssueKind issueKind,
        int issueCount) => new(null, issueKind, issueCount);
}
