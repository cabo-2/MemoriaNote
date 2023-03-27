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
        //

        static readonly char[] invalidChars = new char[] { '\\','/',':','*','?','\"','<','>','|' };
        static readonly string[] invalidStrings = new string[] { "CON", "PRN", "AUX", "NUL", "CLOCKS$" };
        static readonly List<string> invalidNames;

        static TextUtil()
        {
            invalidNames = new List<string>();
            invalidNames.AddRange(Enumerable.Range(0, 10).Select(i => $"COM{i}"));
            invalidNames.AddRange(Enumerable.Range(0, 10).Select(i => $"LPT{i}"));
            invalidNames.AddRange(invalidStrings);
        }

        public static bool ValidateNameString(string name, List<string> errors) {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("The text name have not been entered.");
                return false;
            }

            if (invalidNames.Contains(name))
            {
                errors.Add($"\"{name}\" cannot be used as a text name.");
                return false;
            }

            var errorChars = invalidChars
                .Where(c => name.Contains(c)).ToList();
            if (errorChars.Count > 0) 
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append($"\"");
                foreach(var c in errorChars)
                    buffer.Append($" {c} ");

                buffer.Append("\" cannot be used as a text name.");
                errors.Add(buffer.ToString());
                return false;
            }
            else
                return true;
        }

        public static bool ValidateTextString(string text, List<string> errors) => true;

        public static string ReplaceNameString(string name)
        {
            if (invalidNames.Contains(name))              
                return name + "'";

            string newName = name;
            foreach(var c in invalidChars.Where(c => name.Contains(c)))
                newName = newName.Replace(c, '_');
            
            return newName.Substring(0, Math.Min(newName.Length, 60));
        }
    }
}