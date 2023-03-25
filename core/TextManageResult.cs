using System;
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
    }
}