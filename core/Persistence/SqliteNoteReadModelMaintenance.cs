using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MemoriaNote
{
    /// <summary>
    /// Checks and rebuilds the Contents and FtsIndex SQLite read models from Pages.
    /// </summary>
    public sealed class SqliteNoteReadModelMaintenance : INoteReadModelMaintenance
    {
        static readonly string[] RequiredTriggers =
        {
            "Pages_Insert",
            "Pages_Update",
            "Pages_Delete"
        };

        readonly INoteDatabaseFactory _databaseFactory;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="SqliteNoteReadModelMaintenance"/> class.
        /// </summary>
        /// <param name="databaseFactory">The factory used to create database contexts.</param>
        public SqliteNoteReadModelMaintenance(INoteDatabaseFactory databaseFactory)
        {
            _databaseFactory = databaseFactory ??
                throw new ArgumentNullException(nameof(databaseFactory));
        }

        /// <inheritdoc/>
        public async Task<ReadModelIntegrityReport> CheckIntegrityAsync(
            string dataSource,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);
            var report = await CheckIntegrityAsync(context, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return report;
        }

        /// <inheritdoc/>
        public async Task<ReadModelIntegrityReport> RebuildAsync(
            string dataSource,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var context = _databaseFactory.Create(dataSource);
            await using var transaction = await context.Database
                .BeginTransactionAsync(token)
                .ConfigureAwait(false);

            await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Contents;",
                    token)
                .ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO Contents(
                        Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                        CreateTime, UpdateTime, IsErased)
                      SELECT
                        Rowid, Uuid, Name, ""Index"", Tags, ContentType,
                        CreateTime, UpdateTime, IsErased
                      FROM Pages;",
                    token)
                .ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO FtsIndex(FtsIndex) VALUES('rebuild');",
                    token)
                .ConfigureAwait(false);

            var report = await CheckIntegrityAsync(context, token).ConfigureAwait(false);
            if (report.IsConsistent)
            {
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return report;
            }

            await transaction.RollbackAsync(token).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
            return await CheckIntegrityAsync(context.DataSource, token).ConfigureAwait(false);
        }

        static async Task<ReadModelIntegrityReport> CheckIntegrityAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            var pages = await ReadPagesAsync(context, token).ConfigureAwait(false);
            var contents = await ReadContentsAsync(context, token).ConfigureAwait(false);
            var issues = CompareRows(pages, contents);
            var missingTriggers = await ReadMissingTriggersAsync(context, token)
                .ConfigureAwait(false);
            foreach (var trigger in missingTriggers)
            {
                issues.Add(new ReadModelIntegrityIssue(
                    ReadModelIntegrityIssueKind.MissingTrigger,
                    propertyName: trigger));
            }

            var ftsIndexExists = await FtsIndexExistsAsync(context, token)
                .ConfigureAwait(false);
            var ftsIndexIsConsistent = false;
            if (!ftsIndexExists)
            {
                issues.Add(new ReadModelIntegrityIssue(
                    ReadModelIntegrityIssueKind.MissingFtsIndex,
                    propertyName: "FtsIndex"));
            }
            else
            {
                ftsIndexIsConsistent = await CheckFtsIndexAsync(context, token)
                    .ConfigureAwait(false);
                if (!ftsIndexIsConsistent)
                {
                    issues.Add(new ReadModelIntegrityIssue(
                        ReadModelIntegrityIssueKind.FtsIndexMismatch,
                        propertyName: "FtsIndex"));
                }
            }

            return new ReadModelIntegrityReport(
                context.DataSource,
                pages.Count,
                contents.Count,
                ftsIndexIsConsistent,
                missingTriggers,
                issues);
        }

        static async Task<List<ReadModelRow>> ReadPagesAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            return await context.Pages
                .AsNoTracking()
                .Select(page => new ReadModelRow
                {
                    RowId = page.Rowid,
                    Uuid = page.Uuid,
                    Name = page.Name,
                    Index = page.Index,
                    Tags = page.Tags,
                    ContentType = page.ContentType,
                    CreateTime = page.CreateTime,
                    UpdateTime = page.UpdateTime,
                    IsErased = page.IsErased
                })
                .ToListAsync(token)
                .ConfigureAwait(false);
        }

        static async Task<List<ReadModelRow>> ReadContentsAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            return await context.Contents
                .AsNoTracking()
                .Select(content => new ReadModelRow
                {
                    RowId = content.Rowid,
                    Uuid = content.Uuid,
                    Name = content.Name,
                    Index = content.Index,
                    Tags = content.Tags,
                    ContentType = content.ContentType,
                    CreateTime = content.CreateTime,
                    UpdateTime = content.UpdateTime,
                    IsErased = content.IsErased
                })
                .ToListAsync(token)
                .ConfigureAwait(false);
        }

        static List<ReadModelIntegrityIssue> CompareRows(
            IReadOnlyList<ReadModelRow> pages,
            IReadOnlyList<ReadModelRow> contents)
        {
            var issues = new List<ReadModelIntegrityIssue>();
            AddDuplicateIssues(issues, pages, "Pages");
            AddDuplicateIssues(issues, contents, "Contents");

            var pagesByUuid = pages.GroupBy(row => row.Uuid)
                .ToDictionary(group => group.Key, group => group.ToList());
            var contentsByUuid = contents.GroupBy(row => row.Uuid)
                .ToDictionary(group => group.Key, group => group.ToList());
            foreach (var page in pages)
            {
                if (!contentsByUuid.TryGetValue(page.Uuid, out var matches))
                {
                    issues.Add(new ReadModelIntegrityIssue(
                        ReadModelIntegrityIssueKind.MissingContent,
                        page.RowId,
                        page.Uuid));
                    continue;
                }

                if (matches.Count == 1)
                    CompareValues(issues, page, matches[0]);
            }

            foreach (var content in contents)
            {
                if (!pagesByUuid.ContainsKey(content.Uuid))
                {
                    issues.Add(new ReadModelIntegrityIssue(
                        ReadModelIntegrityIssueKind.UnexpectedContent,
                        content.RowId,
                        content.Uuid));
                }
            }

            var pagesByRowId = pages.GroupBy(row => row.RowId)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            var contentsByRowId = contents.GroupBy(row => row.RowId)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            foreach (var pair in pagesByRowId)
            {
                if (contentsByRowId.TryGetValue(pair.Key, out var content) &&
                    !string.Equals(pair.Value.Uuid, content.Uuid, StringComparison.Ordinal))
                {
                    issues.Add(new ReadModelIntegrityIssue(
                        ReadModelIntegrityIssueKind.IdentityMismatch,
                        pair.Key,
                        pair.Value.Uuid,
                        nameof(ReadModelRow.Uuid),
                        pair.Value.Uuid,
                        content.Uuid));
                }
            }

            return issues;
        }

        static void AddDuplicateIssues(
            ICollection<ReadModelIntegrityIssue> issues,
            IEnumerable<ReadModelRow> rows,
            string modelName)
        {
            foreach (var group in rows.GroupBy(row => row.RowId).Where(group => group.Count() > 1))
            {
                issues.Add(new ReadModelIntegrityIssue(
                    ReadModelIntegrityIssueKind.DuplicateRowId,
                    group.Key,
                    propertyName: modelName + ".Rowid",
                    actualValue: group.Count().ToString(CultureInfo.InvariantCulture)));
            }

            foreach (var group in rows.GroupBy(row => row.Uuid).Where(group => group.Count() > 1))
            {
                issues.Add(new ReadModelIntegrityIssue(
                    ReadModelIntegrityIssueKind.DuplicateUuid,
                    uuid: group.Key,
                    propertyName: modelName + ".Uuid",
                    actualValue: group.Count().ToString(CultureInfo.InvariantCulture)));
            }
        }

        static void CompareValues(
            ICollection<ReadModelIntegrityIssue> issues,
            ReadModelRow page,
            ReadModelRow content)
        {
            if (page.RowId != content.RowId)
            {
                AddValueIssue(
                    issues,
                    ReadModelIntegrityIssueKind.IdentityMismatch,
                    page,
                    nameof(ReadModelRow.RowId),
                    page.RowId,
                    content.RowId);
            }

            CompareValue(issues, page, nameof(ReadModelRow.Name), page.Name, content.Name);
            CompareValue(issues, page, nameof(ReadModelRow.Index), page.Index, content.Index);
            CompareValue(issues, page, nameof(ReadModelRow.Tags), page.Tags, content.Tags);
            CompareValue(
                issues,
                page,
                nameof(ReadModelRow.ContentType),
                page.ContentType,
                content.ContentType);
            CompareValue(
                issues,
                page,
                nameof(ReadModelRow.CreateTime),
                page.CreateTime,
                content.CreateTime);
            CompareValue(
                issues,
                page,
                nameof(ReadModelRow.UpdateTime),
                page.UpdateTime,
                content.UpdateTime);
            CompareValue(
                issues,
                page,
                nameof(ReadModelRow.IsErased),
                page.IsErased,
                content.IsErased);
        }

        static void CompareValue<T>(
            ICollection<ReadModelIntegrityIssue> issues,
            ReadModelRow page,
            string propertyName,
            T expected,
            T actual)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
                return;

            AddValueIssue(
                issues,
                ReadModelIntegrityIssueKind.ValueMismatch,
                page,
                propertyName,
                expected,
                actual);
        }

        static void AddValueIssue<T>(
            ICollection<ReadModelIntegrityIssue> issues,
            ReadModelIntegrityIssueKind kind,
            ReadModelRow page,
            string propertyName,
            T expected,
            T actual)
        {
            issues.Add(new ReadModelIntegrityIssue(
                kind,
                page.RowId,
                page.Uuid,
                propertyName,
                FormatValue(expected),
                FormatValue(actual)));
        }

        static string FormatValue<T>(T value)
        {
            if (value == null)
                return null;
            if (value is DateTime dateTime)
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        static async Task<IReadOnlyList<string>> ReadMissingTriggersAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
            using var command = CreateCommand(
                context,
                @"SELECT name
                  FROM sqlite_master
                  WHERE type = 'trigger'
                    AND name IN ('Pages_Insert', 'Pages_Update', 'Pages_Delete');");
            using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var existing = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                existing.Add(reader.GetString(0));

            return RequiredTriggers.Where(trigger => !existing.Contains(trigger)).ToList();
        }

        static async Task<bool> FtsIndexExistsAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
            using var command = CreateCommand(
                context,
                @"SELECT EXISTS(
                    SELECT 1 FROM sqlite_master
                    WHERE type = 'table' AND name = 'FtsIndex');");
            var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        static async Task<bool> CheckFtsIndexAsync(
            NoteDbContext context,
            CancellationToken token)
        {
            await context.Database.OpenConnectionAsync(token).ConfigureAwait(false);
            using var command = CreateCommand(
                context,
                "INSERT INTO FtsIndex(FtsIndex, rank) VALUES('integrity-check', 1);");
            try
            {
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return true;
            }
            catch (SqliteException exception) when (
                exception.SqliteErrorCode == 11 ||
                exception.SqliteExtendedErrorCode == 267)
            {
                return false;
            }
        }

        static DbCommand CreateCommand(NoteDbContext context, string commandText)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            if (context.Database.CurrentTransaction != null)
            {
                command.Transaction = context.Database.CurrentTransaction
                    .GetDbTransaction();
            }

            return command;
        }

        sealed class ReadModelRow
        {
            internal int RowId { get; set; }

            internal string Uuid { get; set; }

            internal string Name { get; set; }

            internal int Index { get; set; }

            internal string Tags { get; set; }

            internal string ContentType { get; set; }

            internal DateTime CreateTime { get; set; }

            internal DateTime UpdateTime { get; set; }

            internal bool IsErased { get; set; }
        }
    }
}
