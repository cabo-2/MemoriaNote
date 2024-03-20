using System;
using System.Text;
using System.Collections.Generic;

namespace MemoriaNote
{
    /// <summary>
    /// Class representing the result of a text management operation
    /// </summary>
    public class TextManageResult
    {
        /// <summary>
        /// Indicates whether the operation was successful or not
        /// </summary>
        /// <value></value>
        public bool Result { get; set; }
        /// <summary>
        /// The type of the text management operation
        /// </summary>
        /// <value></value>
        public TextManageType Operation { get; set; }
        /// <summary>
        /// The content of the text
        /// </summary>
        /// <value></value>
        public Content Content { get; set; }
        /// <summary>
        /// Notification message related to the operation
        /// </summary>
        /// <value></value>
        public string Notification { get; set; }
        /// <summary>
        /// List of errors that occurred during the operation
        /// </summary>
        /// <value></value>
        public List<string> Errors { get; set; }
    
        /// <summary>
        /// Override the ToString method to provide a custom string representation of the object
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Mange: ");
            builder.Append(Operation.ToString());
            builder.Append(", Result: ");
            builder.Append(Result.ToString());
            builder.Append(", Notify: ");
            builder.Append(Notification);            
            return builder.ToString();
        }
    }
}