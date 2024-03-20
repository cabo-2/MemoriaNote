using System;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Represents a data source in a system.
    /// </summary>
    public interface IDataSource : IEquatable<IDataSource>
    {
        /// <summary>
        /// The name of the data source.
        /// </summary>
        public string Name { get; set; }
    
        /// <summary>
        /// The title of the data source.
        /// </summary>
        public string Title { get; set; }
    
        /// <summary>
        /// The version of the data source.
        /// </summary>
        public string Version { get; set; }       
    
        /// <summary>
        /// The description of the data source.
        /// </summary>
        public string Description { get; set; }
    
        /// <summary>
        /// The author of the data source.
        /// </summary>
        public string Author { get; set; }        
    
        /// <summary>
        /// Indicates if the data source is read-only.
        /// </summary>
        public bool ReadOnly { get; set; }        
    
        /// <summary>
        /// A tag associated with the data source.
        /// </summary>
        public string Tag { get; set; }
    
        /// <summary>
        /// The date and time when the data source was created.
        /// </summary>
        public DateTime CreateTime { get; set; }
    
        /// <summary>
        /// The actual data of the data source.
        /// </summary>
        public string DataSource { get; set; }

        /// <summary>
        /// Copies the data source to the destination data source.
        /// </summary>
        /// <param name="dest"></param>
        public void CopyTo(IDataSource dest);    
        public string ToString();
        public IDataSource Clone();
    }
}