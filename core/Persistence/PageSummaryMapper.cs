using System;

namespace MemoriaNote
{
    static class PageSummaryMapper
    {
        internal static PageSummary FromContent(NoteId noteId, IContent content)
        {
            if (noteId == null)
                throw new ArgumentNullException(nameof(noteId));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return new PageSummary(
                noteId,
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
