using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MemoriaNote.Transfer
{
    /// <summary>
    /// Converts text-transfer directory paths between portable and system forms.
    /// </summary>
    public sealed class TextTransferPathCodec
    {
        readonly PageFileNameCodec _fileNameCodec;

        /// <summary>
        /// Initializes a text-transfer path codec.
        /// </summary>
        /// <param name="fileNameCodec">The codec applied to each path component.</param>
        public TextTransferPathCodec(PageFileNameCodec fileNameCodec)
        {
            _fileNameCodec = fileNameCodec ??
                throw new ArgumentNullException(nameof(fileNameCodec));
        }

        /// <summary>
        /// Encodes a portable relative path for the current operating system.
        /// </summary>
        /// <param name="relativePath">A slash-separated relative path.</param>
        /// <returns>A relative path using system separators and portable components.</returns>
        public string EncodeRelativePath(string relativePath)
        {
            if (relativePath == null)
                return null;
            if (relativePath.Length == 0)
                return string.Empty;

            EnsureRelativePath(relativePath);
            var components = relativePath.Split('/');
            EnsureSafeComponents(components, relativePath);
            return CombineComponents(
                components.Select(component => _fileNameCodec.Encode(component)));
        }

        /// <summary>
        /// Decodes a system relative path into slash-separated portable form.
        /// </summary>
        /// <param name="relativePath">A relative path using system separators.</param>
        /// <returns>A slash-separated path with decoded components.</returns>
        public string DecodeRelativePath(string relativePath)
        {
            if (relativePath == null)
                return null;
            if (relativePath.Length == 0)
                return string.Empty;

            EnsureRelativePath(relativePath);
            var separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
                ? new[] { Path.DirectorySeparatorChar }
                : new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var components = relativePath.Split(separators);
            EnsureSafeComponents(components, relativePath);
            return string.Join(
                "/",
                components.Select(component => _fileNameCodec.Decode(component)));
        }

        static void EnsureRelativePath(string path)
        {
            if (Path.IsPathRooted(path) ||
                path[0] == '/' ||
                path[0] == '\\' ||
                IsDriveQualified(path))
            {
                throw new ArgumentException("The transfer path must be relative.", nameof(path));
            }
        }

        static bool IsDriveQualified(string path)
        {
            return path.Length >= 3 &&
                char.IsLetter(path[0]) &&
                path[1] == ':' &&
                (path[2] == '/' || path[2] == '\\');
        }

        static void EnsureSafeComponents(IEnumerable<string> components, string path)
        {
            if (components.Any(component =>
                component.Length == 0 || component == "." || component == ".."))
            {
                throw new ArgumentException(
                    "The transfer path contains an unsafe component.",
                    nameof(path));
            }
        }

        static string CombineComponents(IEnumerable<string> components)
        {
            return components.Aggregate(Path.Combine);
        }
    }
}
