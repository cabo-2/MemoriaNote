using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    static class PageSummaryContentAdapter
    {
        internal static Content ToContent(PageSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            return new Content()
            {
                Guid = summary.PageId.Value,
                Name = summary.Name,
                Index = summary.Index,
                TagDict = new Dictionary<string, string>(summary.Tags),
                ContentType = summary.ContentType,
                CreateTime = summary.CreateTime,
                UpdateTime = summary.UpdateTime,
                IsErased = summary.IsErased,
                OwnerDataSource = summary.NoteId.Locator
            };
        }
    }
}
