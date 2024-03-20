using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MemoriaNote
{
    /// <summary>
    /// This class provides functionality for creating temporary files in a specified directory and managing them.
    /// </summary>
    public class Scratchpad
    {
        /// <summary>
        /// Singleton instance of the Scratchpad class
        /// </summary>
        public static Scratchpad Singleton { get; } = new Scratchpad(Configuration.ApplicationName);

        string _tempDir;
        /// <summary>
        /// List to keep track of created temporary files
        /// </summary>
        protected List<string> _tempFiles;

        Scratchpad(string name)
        {            
            _tempFiles = new List<string>();
            _tempDir = Path.Combine(Path.GetTempPath(), name);
        }

        // Default file extension for temporary files
        static string DefaultExt => ".txt";

        // Generates a random name for a file
        static string GetRandomName() => Path.GetRandomFileName().Replace(".", "");

        /// <summary>
        /// Method to get a temporary file path
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="random"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
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

        /// <summary>
        /// Method to clear a specific temporary file
        /// </summary>
        /// <param name="tempPath"></param>
        /// <exception cref="ArgumentException"></exception>
        public void Clear(string tempPath)
        {
            if(!_tempFiles.Any( path => path == tempPath ))
                throw new ArgumentException(nameof(Clear));
            
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            _tempFiles.Remove(tempPath);
        }

        /// <summary>
        /// Method to clear all created temporary files
        /// </summary>
        public void ClearAll()
        {
            foreach(var path in _tempFiles)
                Clear(path);
        }

        /// <summary>
        /// Property to get the working directory where temporary files are stored
        /// </summary>
        public string WorkDirectory => _tempDir;

        /// <summary>
        /// Property to get a read-only collection of created temporary file paths
        /// </summary>
        public ReadOnlyCollection<string> CreatedFiles => new ReadOnlyCollection<string>(_tempFiles);
    }
}