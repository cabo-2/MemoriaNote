using System;

namespace MemoriaNote
{
    static class PageSummaryMapper
    {
        internal static PageSummary FromContent(NotebookId notebookId, IContent content)
        {
            if (notebookId == null)
                throw new ArgumentNullException(nameof(notebookId));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return new PageSummary(
                notebookId,
                PageId.FromUuid(content.Uuid),
                content.Name,
                content.Index,
                content.TagDict,
                content.ContentType,
                content.CreateTime,
                content.UpdateTime,
                content.IsErased);
        }
    }
}
