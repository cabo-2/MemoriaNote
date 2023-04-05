using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace MemoriaNote
{
    public static class TextUtil    
    {
        //
        // Invalid Name Patterns
        //  WindowsFS compatible: \/:*?\<>| COM0-9 LPT0-9 CON PRN AUX NUL CLOCKS$
        //  LinuxFS compatible  : /
        //

        static readonly ReplaceChar[] replaceChars = new ReplaceChar[] 
        {
            //new ReplaceChar() { Invalid = '\\', Replace = '＼' },
            new ReplaceChar() { Invalid = '/', Replace = '／' },
            //new ReplaceChar() { Invalid = ':', Replace = '：' },
            //new ReplaceChar() { Invalid = '*', Replace = '＊' },
            //new ReplaceChar() { Invalid = '?', Replace = '？' },
            //new ReplaceChar() { Invalid = '"', Replace = '”' },
            //new ReplaceChar() { Invalid = '<', Replace = '＜' },
            //new ReplaceChar() { Invalid = '>', Replace = '＞' },
            //new ReplaceChar() { Invalid = '|', Replace = '｜' }
        };
        static readonly List<string> invalidNames;

        static TextUtil()
        {
            invalidNames = new List<string>();
            invalidNames.AddRange(Enumerable.Range(0, 10).Select(i => $"COM{i}"));
            invalidNames.AddRange(Enumerable.Range(0, 10).Select(i => $"LPT{i}"));
            invalidNames.AddRange(new string[] { "CON", "PRN", "AUX", "NUL", "CLOCKS$" });
        }

        public static bool ValidateNameString(string name, List<string> errors) {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("The text name have not been entered.");
                return false;
            }
            return true;
        }

        public static bool ValidateTextString(string text, List<string> errors) => true;

        public static string ReplaceNameString(string name)
        {
            if (invalidNames.Contains(name))              
                return name + "＿";

            foreach(var r in replaceChars)
                if(name.Contains(r.Invalid))
                    name = name.Replace(r.Invalid, r.Replace);
            
            return name.Substring(0, Math.Min(name.Length, 128));
        }

        public static string ReplaceNameStringReverse(string name)
        {
            if (invalidNames.Any(x => (x + "＿") == name))              
                return name.Substring(0, name.Length - 1);

            foreach(var r in replaceChars)
                if(name.Contains(r.Replace))
                    name = name.Replace(r.Replace, r.Invalid);
            
            return name.Substring(0, Math.Min(name.Length, 128));
        }

        public class ReplaceChar
        {
            public char Invalid { get; set; }
            public char Replace { get; set; }
        }
    }
}