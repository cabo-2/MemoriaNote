using System;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Persistence
{
    static class PageSummaryMapper
    {
        internal static PageSummary FromRecord(
            NotebookId notebookId,
            PageSummaryRecord record)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            return new PageSummary(
                notebookId,
                PageId.FromUuid(record.Uuid),
                record.Name,
                record.Index,
                record.TagDict,
                record.ContentType,
                record.CreateTime,
                record.UpdateTime,
                record.IsErased);
        }

        internal static PageSummary FromPage(NotebookId notebookId, Page page)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            return new PageSummary(
                notebookId,
                PageId.FromGuid(page.Guid),
                page.Name,
                page.Index,
                page.TagDict,
                page.ContentType,
                page.CreateTime,
                page.UpdateTime,
                page.IsErased);
        }
    }
}
