using System;
using System.Collections.Generic;
using System.Text;

namespace MemoriaNote
{
    /// <summary>
    /// Represents the results of a search operation, including the list of content, start and end times, and the count of results.
    /// </summary>
    public class SearchResult
    {
        /// <summary>
        /// Represents the search results containing a list of content, start and end times, and the count of results.
        /// </summary>
        /// <value>The list of content in the search results.</value>
        public List<Content> Contents { get; set; }

        /// <summary>
        /// Gets or sets the start time of the search operation.
        /// </summary>
        /// <value>The start time of the search operation.</value>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the end time of the search operation.
        /// </summary>
        /// <value>The end time of the search operation.</value>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Gets or sets the count of results in the search operation.
        /// </summary>
        /// <value>The count of results in the search operation.</value>
        public int Count { get; set; }

        /// <summary>
        /// Concatenates two search results into a new SearchResult object.
        /// </summary>
        /// <param name="a">The first search result to concatenate.</param>
        /// <param name="b">The second search result to concatenate.</param>
        /// <returns>The combined search result.</returns>
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

        /// <summary>
        /// Represents an empty search result.
        /// </summary>
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

        /// <summary>
        /// Initializes a new instance of the SearchResult class with an empty list of content.
        /// </summary>
        public SearchResult()
        {
            Contents = new List<Content>();
        }

        /// <summary>
        /// Returns a string representation of the search result.
        /// </summary>
        /// <returns>A string with the count of results and response time.</returns>
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
