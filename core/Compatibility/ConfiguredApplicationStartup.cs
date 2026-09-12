using System;
using System.IO;

namespace MemoriaNote
{
    /// <summary>
    /// Checks file-backed note data sources for the compatibility composition root.
    /// </summary>
    internal sealed class FileNoteDataSourceProbe : INoteDataSourceProbe
    {
        /// <inheritdoc/>
        public bool Exists(string dataSource)
        {
            return File.Exists(dataSource);
        }
    }

    /// <summary>
    /// Builds a workgroup from the current compatibility configuration.
    /// </summary>
    internal sealed class ConfiguredWorkgroupLoader : IWorkgroupLoader
    {
        readonly WorkgroupBuilder _builder;

        internal ConfiguredWorkgroupLoader(WorkgroupBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <inheritdoc/>
        public Workgroup Load()
        {
            return _builder.Build();
        }
    }
}
