using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// Describes the data sources and default note required to start the application.
    /// </summary>
    public sealed class ApplicationStartupRequest
    {
        readonly IReadOnlyList<string> _dataSources;

        /// <summary>
        /// Initializes an application startup request.
        /// </summary>
        /// <param name="defaultNoteName">The default note name.</param>
        /// <param name="defaultNoteTitle">The default note title.</param>
        /// <param name="defaultDataSource">The default note data source.</param>
        /// <param name="dataSources">The data sources to migrate in order.</param>
        public ApplicationStartupRequest(
            string defaultNoteName,
            string defaultNoteTitle,
            string defaultDataSource,
            IEnumerable<string> dataSources)
        {
            if (string.IsNullOrWhiteSpace(defaultNoteName))
                throw new ArgumentException("The default note name is required.", nameof(defaultNoteName));
            if (string.IsNullOrWhiteSpace(defaultNoteTitle))
                throw new ArgumentException("The default note title is required.", nameof(defaultNoteTitle));
            if (string.IsNullOrWhiteSpace(defaultDataSource))
                throw new ArgumentException("The default data source is required.", nameof(defaultDataSource));
            if (dataSources == null)
                throw new ArgumentNullException(nameof(dataSources));

            var copiedDataSources = dataSources.ToArray();
            if (copiedDataSources.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Migration data sources cannot contain empty values.",
                    nameof(dataSources));
            }

            DefaultNoteName = defaultNoteName;
            DefaultNoteTitle = defaultNoteTitle;
            DefaultDataSource = defaultDataSource;
            _dataSources = new ReadOnlyCollection<string>(copiedDataSources);
        }

        /// <summary>Gets the default note name.</summary>
        public string DefaultNoteName { get; }

        /// <summary>Gets the default note title.</summary>
        public string DefaultNoteTitle { get; }

        /// <summary>Gets the default note data source.</summary>
        public string DefaultDataSource { get; }

        /// <summary>Gets the data sources to migrate in order.</summary>
        public IReadOnlyList<string> DataSources => _dataSources;
    }
}
