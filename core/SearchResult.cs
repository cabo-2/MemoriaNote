using System;
using System.Collections.Generic;
using System.Text;

namespace MemoriaNote
{
    public class SearchResult
    {
        public List<Content> Contents { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Count { get; set; }

        public static SearchResult Concat(SearchResult a, SearchResult b)
        {
            var sr = new SearchResult();
            sr.Contents = new List<Content>();
            sr.Contents.AddRange(a.Contents);
            sr.Contents.AddRange(b.Contents);
            sr.StartTime = a.StartTime < b.StartTime ? a.StartTime : b.StartTime;
            sr.EndTime = a.EndTime > b.EndTime ? a.EndTime : b.EndTime;
            sr.Count = a.Count + b.Count;
            return sr;
        }

        public static SearchResult Empty
        {
            get
            {
                return new SearchResult()
                {
                    Contents = new List<Content>(),
                    StartTime = new DateTime(),
                    EndTime = new DateTime(),
                    Count = 0
                };
            }
        }

        public SearchResult()
        {
            Contents = new List<Content>();
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(GetResultCountToStrung());
            builder.Append(" ( ");
            builder.Append(GetResponseTimeToString());
            builder.Append(" )");
            return builder.ToString();
        }

        private string GetResultCountToStrung()
        {
            if (Count <= 0)
                return "No results found";

            return Count + " results found";
        }

        private string GetResponseTimeToString()
        {
            var span = EndTime - StartTime;
            if (span.TotalHours > 1.0)
                return Math.Round(span.TotalHours, 2).ToString() + " hours";
            if (span.TotalMinutes > 1.0)
                return Math.Round(span.TotalHours, 2).ToString() + " minutes";

            return Math.Round(span.TotalSeconds, 2).ToString() + " seconds";
        }
    }
}
