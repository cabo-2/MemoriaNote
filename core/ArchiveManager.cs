using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;

namespace MemoriaNote
{
    public class ArchiveManager : ReactiveObject
    {
        public ArchiveManager()
        {}

        public void Migrate()
        {
            foreach (var dataSource in Configuration.Instance.DataSources)
                Note.Migrate(dataSource);
        }

        public void Load()
        {
            var dataSources = Configuration.Instance.DataSources;
            var groupBuilders = Configuration.Instance.BookGroupBuilders;
            Open(dataSources, groupBuilders);
            Configuration.Instance.BookGroupBuilders = GetBookGroupBuilders();
        }

        public void Open(List<string> dataSources, List<BookGroupBuilder> groupBuilders)
        {
            var bookGroup = new List<BookGroup>();
            foreach (var builder in groupBuilders)
            {
                var books = new BookGroup()
                {
                    Name = builder.Name,
                    SearchRange = builder.SearchRange,
                    IsAutoEnabled = builder.IsAutoEnabled,
                    Collection = GetBookGroupItems(builder, dataSources)
                };
                books.CurrentNote = books.EnabledNotes.FirstOrDefault();
                bookGroup.Add(books);
            }
            _bookGroup = bookGroup;
            _currentBooks = _bookGroup.FirstOrDefault();

            this.RaisePropertyChanged(nameof(CurrentBooks));
            this.RaisePropertyChanged(nameof(BookGroup));
        }

        List<BookGroupItem> GetBookGroupItems(BookGroupBuilder builder, IEnumerable<string> dataSources)
        {
            List<BookGroupItem> items = new List<BookGroupItem>();
            foreach(var source in dataSources)
            {
                var item = new BookGroupItem();
                item.Note = new Note(source);               

                if (builder.UseDataSources.Contains(source))
                    item.IsEnabled = true;
                else if (builder.IsAutoEnabled)
                    item.IsEnabled = true;
                else
                    item.IsEnabled = false;
                item.Priority = ushort.MaxValue;

                items.Add(item);
            }
            ushort priority = 0;
            foreach(var enableSource in builder.UseDataSources)
            {
                var item = items.FirstOrDefault(i => i.Note.DataSource == enableSource);
                if (item != null)
                    item.Priority = priority++;
            }
            return items;
        }       

        public List<BookGroupBuilder> GetBookGroupBuilders()
        {
            var list = new List<BookGroupBuilder>(); 
            foreach(var books in _bookGroup)
            {
                var builder = new BookGroupBuilder()
                {
                    Name = books.Name,
                    SearchRange = books.SearchRange,
                    IsAutoEnabled = books.IsAutoEnabled,
                    UseDataSources = books.EnabledNotes.Select(b => b.DataSource).ToList()
                };
                list.Add(builder);
            }
            return list;
        }

        protected BookGroup _currentBooks = null;
        public BookGroup CurrentBooks
        {
            get => _currentBooks;
            set => this.RaiseAndSetIfChanged(ref _currentBooks, value);
        }

        protected IList<BookGroup> _bookGroup = new List<BookGroup>();
        public IList<BookGroup> BookGroup
        {
            get => new ReadOnlyCollection<BookGroup>(_bookGroup);
        }
    }
}
