using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MemoriaNote
{
    /// <summary>
    /// Loads and updates note metadata stored as SQLite key-value rows.
    /// </summary>
    public sealed class SqliteNoteMetadataRepository : INoteMetadataRepository
    {
        const string StoredCreateTimeFormat = "yyyyMMddhhmmss";
        readonly INoteDatabaseFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteNoteMetadataRepository"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNoteMetadataRepository(INoteDatabaseFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        /// <inheritdoc/>
        public async Task<MetadataLoadResult> LoadAsync(
            string dataSource,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            var values = await ReadValuesAsync(context, token).ConfigureAwait(false);
            return CreateResult(context.DataSource, values);
        }

        /// <inheritdoc/>
        public async Task<MetadataLoadResult> UpdateAsync(
            string dataSource,
            NoteMetadataUpdate update,
            CancellationToken token)
        {
            if (update == null)
                throw new ArgumentNullException(nameof(update));

            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var entities = await context.Metadata
                .ToDictionaryAsync(entry => entry.Key, token)
                .ConfigureAwait(false);

            foreach (var change in update.Values)
            {
                if (entities.TryGetValue(change.Key, out var entity))
                {
                    entity.Value = change.Value;
                }
                else
                {
                    entity = new NoteKeyValue
                    {
                        Key = change.Key,
                        Value = change.Value
                    };
                    context.Metadata.Add(entity);
                    entities.Add(change.Key, entity);
                }
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);

            var values = new Dictionary<string, string>();
            foreach (var entity in entities.Values)
                values[entity.Key] = entity.Value;

            return CreateResult(context.DataSource, values);
        }

        static async Task<Dictionary<string, string>> ReadValuesAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            var entities = await context.Metadata
                .AsNoTracking()
                .ToListAsync(token)
                .ConfigureAwait(false);
            var values = new Dictionary<string, string>();
            foreach (var entity in entities)
                values[entity.Key] = entity.Value;

            return values;
        }

        static MetadataLoadResult CreateResult(
            string dataSource,
            IReadOnlyDictionary<string, string> values)
        {
            var issues = new List<MetadataLoadIssue>();
            var name = GetRequiredValue(values, NoteKeyValue.Name, issues);
            var title = GetRequiredValue(values, NoteKeyValue.Title, issues);
            var version = GetRequiredValue(values, NoteKeyValue.Version, issues);
            values.TryGetValue(NoteKeyValue.Description, out var description);
            values.TryGetValue(NoteKeyValue.Author, out var author);
            values.TryGetValue(NoteKeyValue.Tag, out var tag);

            var readOnly = false;
            if (values.TryGetValue(NoteKeyValue.ReadOnly, out var readOnlyValue) &&
                readOnlyValue != null &&
                !bool.TryParse(readOnlyValue, out readOnly))
            {
                issues.Add(new MetadataLoadIssue(
                    MetadataLoadIssueKind.InvalidBoolean,
                    NoteKeyValue.ReadOnly,
                    readOnlyValue));
                readOnly = false;
            }

            var createTime = default(DateTime);
            if (values.TryGetValue(NoteKeyValue.CreateTime, out var createTimeValue) &&
                createTimeValue != null &&
                !DateTime.TryParseExact(
                    createTimeValue,
                    StoredCreateTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out createTime))
            {
                issues.Add(new MetadataLoadIssue(
                    MetadataLoadIssueKind.InvalidCreateTime,
                    NoteKeyValue.CreateTime,
                    createTimeValue));
                createTime = default(DateTime);
            }

            var metadata = new NoteMetadata(
                dataSource,
                name,
                title,
                version,
                description,
                author,
                readOnly,
                tag,
                createTime,
                values);
            return new MetadataLoadResult(metadata, issues);
        }

        static string GetRequiredValue(
            IReadOnlyDictionary<string, string> values,
            string key,
            ICollection<MetadataLoadIssue> issues)
        {
            if (values.TryGetValue(key, out var value))
                return value;

            issues.Add(new MetadataLoadIssue(
                MetadataLoadIssueKind.MissingKey,
                key,
                null));
            return null;
        }
    }
}
