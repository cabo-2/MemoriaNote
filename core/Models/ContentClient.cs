using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// This class represents a client that interacts with the database to read content entities.
    /// </summary>
    public class ContentClient
    {
        // Default constructor
        public ContentClient() { }
    
        /// <summary>
        /// Constructor that takes a NoteDbContext parameter
        /// </summary>
        /// <param name="dbContext"></param>
        public ContentClient(NoteDbContext dbContext)
        {
            DbContext = dbContext;
        }
    
        /// <summary>
        /// Method to read a content entity by its GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public Content Read(Guid guid)
        {
            return DbContext.Contents.Find(guid.ToUuid());
        }
    
        /// <summary>
        /// Method to read a content entity by its name and index
        /// </summary>
        /// <param name="name"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public Content Read(string name, int index)
        {
            return DbContext.Contents.FirstOrDefault(m => m.Name == name && m.Index == index);
        }
    
        /// <summary>
        /// Method to read all content entities with a specific name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IEnumerable<Content> Read(string name)
        {
            return DbContext.Contents.Where(m => m.Name == name);
        }
    
        /// <summary>
        /// Method to read all content entities
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Content> ReadAll()
        {
            return DbContext.Contents;
        }
        
        /// <summary>
        /// Property to hold the NoteDbContext instance
        /// </summary>
        public NoteDbContext DbContext { get; set; }
    }
}
