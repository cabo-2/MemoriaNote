using System.IO;

namespace MemoriaNote
{
    public interface ITextEditor
    {
        string Text { get; set; }
        void Load (Stream stream);
    }
}
