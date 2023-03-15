using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace MemoriaNote
{
    public static class NoteHelper
    {
        public static Note CreateOrMigrate(string dataSource, string title = null)
        {
            Note note = null;
            if (File.Exists(dataSource))
            {
                note = Note.Migrate(dataSource);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dataSource))
                    throw new ArgumentException("dataSource");
                note = Note.Create(title, dataSource);
            }
            return note;
        }

        public static List<Note> GetEnvironmentNoteList()
        {
            return Configuration.Instance.DataSources
                    .Select(s => new Note(s)).ToList();
        }

        public static List<Note> GetNoteList(IEnumerable<string> dataSources)
        {
            return dataSources
                    .Select(d => new Note(d)).ToList();
        }

        public static bool IsDuplicate(Note note)
        {
            var notes = GetEnvironmentNoteList();
            return notes.Any(n => n.DataSource == note.DataSource);
        }

        public static bool IsSystemNote(Note note)
        {
            var id = NoteDbContext.GenerateID(Configuration.SystemNoteName);
            return note.TitlePage.Noteid == id;
        }

        public static Note GetSystemNote()
        {
            return GetEnvironmentNoteList().FirstOrDefault(n => IsSystemNote(n));
        }
    }
}
