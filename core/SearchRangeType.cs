using System;
using System.Collections.Generic;
using System.Linq;
namespace MemoriaNote
{
    public enum SearchRangeType : int
    {
        Note = 0,
        Workgroup = 1
    }

    public enum SearchMethodType : int
    {
        Headline = 0,
        FullText = 1
    }

    public class EnumTypeConverter
    {
        static string ToString<T> (T value) where T : Enum => value.ToString();  

        public static string ToString(SearchRangeType value) => ToString<SearchRangeType>(value);
        public static string ToString(SearchMethodType value) => ToString<SearchMethodType>(value);

        static T ToEnum<T>(string str) where T : Enum => Enum.GetValues (typeof (T)).OfType<T>().First( value => str == value.ToString() );

        public static SearchRangeType ToSearchRangeType(string str) => ToEnum<SearchRangeType>(str);
        public static SearchMethodType ToSearchMethodType(string str) => ToEnum<SearchMethodType>(str); 
    }
}
