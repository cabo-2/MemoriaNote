namespace MemoriaNote
{
    public static class BookGroupHelper
    {
        public static bool IsSystemBookGroup(IBookGroup books)
        {
            return Configuration.AllNotesSearchString == books.Name ||
                   Configuration.SelectedNoteSearchString == books.Name;
        }
    }
}
