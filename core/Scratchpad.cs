using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote
{
    public class Scratchpad
    {
        public static Scratchpad Singleton { get; } = new Scratchpad(Configuration.ApplicationName);

        string _tempDir;
        protected List<string> _tempFiles;

        Scratchpad(string name)
        {            
            _tempFiles = new List<string>();
            _tempDir = Path.Combine(Path.GetTempPath(), name);
        }

        static string DefaultExt => ".txt";

        static string GetRandomName() => Path.GetRandomFileName().Replace(".", "");

        public string GetFile(string filename = null, bool random = true)
        {
            if (!Directory.Exists(_tempDir))
                Directory.CreateDirectory(_tempDir);

            string tempPath;
            if (filename != null)
            {
                var name = Path.GetFileNameWithoutExtension(filename);
                var ext = new string(filename.Skip(name.Length).ToArray());
                tempPath = Path.Combine(_tempDir, 
                                name + (random ? "_" + GetRandomName() : "") + ext);
            }
            else
            {
                tempPath = Path.Combine(_tempDir, 
                                GetRandomName() + DefaultExt);
            }

            if (_tempFiles.Any( path => path == tempPath ))
                throw new ArgumentException(nameof(GetFile));          
            
            if (random && File.Exists(tempPath))
                throw new ArgumentException(nameof(GetFile));

            _tempFiles.Add(tempPath);            
            return tempPath;
        }

        public void Clear(string tempPath)
        {
            if(!_tempFiles.Any( path => path == tempPath ))
                throw new ArgumentException(nameof(Clear));
            
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            _tempFiles.Remove(tempPath);
        }

        public void ClearAll()
        {
            foreach(var path in _tempFiles)
                Clear(path);
        }

        public string WorkDirectory => _tempDir;

        public ReadOnlyCollection<string> CreatedFiles => new ReadOnlyCollection<string>(_tempFiles);
    }
}