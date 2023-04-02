using System;
using System.Text;
using System.Collections.Generic;

namespace MemoriaNote
{
    public class TextManageResult
    {
        public bool Result { get; set; }
        public TextManageType Operation { get; set; } 
        public Content Content { get; set; }
        public string Notification { get; set; }       
        public List<string> Errors { get; set; }

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