using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Reports whether a note's derived read models agree with Pages.
    /// </summary>
    public sealed class ReadModelIntegrityReport
    {
        internal ReadModelIntegrityReport(
            string dataSource,
            int pageCount,
            int contentCount,
            bool ftsIndexIsConsistent,
            IEnumerable<string> missingTriggers,
            IEnumerable<ReadModelIntegrityIssue> issues)
        {
            DataSource = dataSource;
            PageCount = pageCount;
            ContentCount = contentCount;
            FtsIndexIsConsistent = ftsIndexIsConsistent;
            MissingTriggers = new ReadOnlyCollection<string>(missingTriggers.ToList());
            Issues = new ReadOnlyCollection<ReadModelIntegrityIssue>(issues.ToList());
        }

        /// <summary>Gets the normalized note database path.</summary>
        public string DataSource { get; }

        /// <summary>Gets the number of authoritative Pages rows.</summary>
        public int PageCount { get; }

        /// <summary>Gets the number of derived Contents rows.</summary>
        public int ContentCount { get; }

        /// <summary>Gets whether the FTS5 index agrees with itself and Pages.</summary>
        public bool FtsIndexIsConsistent { get; }

        /// <summary>Gets the names of required synchronization triggers that are absent.</summary>
        public IReadOnlyList<string> MissingTriggers { get; }

        /// <summary>Gets all detected integrity problems.</summary>
        public IReadOnlyList<ReadModelIntegrityIssue> Issues { get; }

        /// <summary>Gets whether no integrity problems were detected.</summary>
        public bool IsConsistent => Issues.Count == 0;
    }
}
