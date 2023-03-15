using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;

namespace MemoriaNote
{
    public class BookGroupBuilder : ReactiveObject, IBookGroup
    {
        public static BookGroupBuilder GetAllNotesSearch(List<string> useDataSources)
        {
            var builder = new BookGroupBuilder();
            builder.Name = Configuration.AllNotesSearchString;
            builder.SearchRange = SearchRangeType.All;
            builder.IsAutoEnabled = true;
            builder.UseDataSources = useDataSources;
            return builder;
        }

        public static BookGroupBuilder GetSelectedNoteSearch(List<string> useDataSources)
        {
            var builder = new BookGroupBuilder();
            builder.Name = Configuration.SelectedNoteSearchString;
            builder.SearchRange = SearchRangeType.Single;
            builder.IsAutoEnabled = true;
            builder.UseDataSources = useDataSources;
            return builder;
        }

        static string[] searchRangeStrings = null;
        static Dictionary<string, SearchRangeType> searchRangeTables = null;        
        static BookGroupBuilder()
        {
            searchRangeTables = new Dictionary<string, SearchRangeType>();
            var types = (SearchRangeType[])Enum.GetValues(typeof(SearchRangeType));
            foreach (var type in types)
                searchRangeTables.Add(type.ToString(), type);
            searchRangeStrings = searchRangeTables.Select(kv => kv.Key).ToArray();
        }

        public static string[] SearchRangeTypeStrings
        {
            get => searchRangeStrings;
        }        

        public BookGroupBuilder() {}

        protected string _name;
        public string Name {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }
        protected SearchRangeType _searchRange;
        public SearchRangeType SearchRange
        {
            get => _searchRange;
            set
            {
                if (!_searchRange.Equals(value)) {
                    this.RaiseAndSetIfChanged(ref _searchRange, value);
                    this.RaisePropertyChanged(nameof(SearchRangeAsString));
                }
            }
        }
        protected bool _isAutoEnabled;
        public bool IsAutoEnabled
        {
            get => _isAutoEnabled;
            set => this.RaiseAndSetIfChanged(ref _isAutoEnabled, value);
        }
        protected List<string> _useDataSources = new List<string>();
        public List<string> UseDataSources
        {
            get => _useDataSources;
            set => this.RaiseAndSetIfChanged(ref _useDataSources, value);
        }
               
        public string SearchRangeAsString
        {
            get => SearchRange.ToString();
            set => SearchRange = searchRangeTables[value];
        }

        public override string ToString()
        {
            return Name;
        }

        public BookGroupBuilder Clone()
        {
            return new BookGroupBuilder()
            {
                Name = this.Name,
                SearchRange = this.SearchRange,
                IsAutoEnabled = this.IsAutoEnabled,
                UseDataSources = new List<string>(this.UseDataSources)
            };
        }
    }
}
