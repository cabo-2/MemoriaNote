using System;
using System.IO;

namespace MemoriaNote.Domain
{
    /// <summary>
    /// Represents a normalized live-notebook leaf file name.
    /// </summary>
    public sealed class NotebookFileName : IEquatable<NotebookFileName>
    {
        /// <summary>Gets the required live-notebook file extension.</summary>
        public const string Extension = ".mnote";

        const string ReservedRootOption = "--root";
        const string ReservedRootFileName = "--root.mnote";

        NotebookFileName(string value)
        {
            Value = value;
        }

        /// <summary>Gets the normalized leaf file name.</summary>
        public string Value { get; }

        /// <summary>
        /// Creates a normalized notebook file name from command input.
        /// </summary>
        /// <param name="input">The notebook leaf name, with an optional lowercase suffix.</param>
        /// <returns>The normalized notebook file name.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="input"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the input is empty, is not a leaf name, has an unsupported extension, or
        /// uses a reserved name.
        /// </exception>
        public static NotebookFileName FromInput(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input), "A notebook is required.");
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("A notebook is required.", nameof(input));
            if (input.IndexOf('/') >= 0 || input.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "The notebook must be a workspace leaf file name without path separators.",
                    nameof(input));
            }
            if (Path.IsPathRooted(input) || input == "." || input == "..")
            {
                throw new ArgumentException(
                    "The notebook must be a workspace leaf file name.",
                    nameof(input));
            }
            if (string.Equals(input, ReservedRootOption, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The notebook name '{ReservedRootOption}' is reserved.",
                    nameof(input));
            }

            string normalized;
            if (input.EndsWith(Extension, StringComparison.Ordinal))
            {
                normalized = input;
            }
            else if (input.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The notebook file must use the lowercase '{Extension}' extension.",
                    nameof(input));
            }
            else if (!string.IsNullOrEmpty(Path.GetExtension(input)))
            {
                throw new ArgumentException(
                    $"The notebook file must use the '{Extension}' extension.",
                    nameof(input));
            }
            else
            {
                normalized = input + Extension;
            }

            if (string.Equals(normalized, ReservedRootFileName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The notebook name '{ReservedRootFileName}' is reserved.",
                    nameof(input));
            }

            return new NotebookFileName(normalized);
        }

        /// <inheritdoc/>
        public bool Equals(NotebookFileName other)
        {
            return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as NotebookFileName);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        /// <summary>Determines whether two notebook file names are equal.</summary>
        /// <param name="left">The first notebook file name.</param>
        /// <param name="right">The second notebook file name.</param>
        /// <returns>True when both values contain the same normalized leaf name.</returns>
        public static bool operator ==(NotebookFileName left, NotebookFileName right)
        {
            return ReferenceEquals(left, right) || left?.Equals(right) == true;
        }

        /// <summary>Determines whether two notebook file names are different.</summary>
        /// <param name="left">The first notebook file name.</param>
        /// <param name="right">The second notebook file name.</param>
        /// <returns>True when the values contain different normalized leaf names.</returns>
        public static bool operator !=(NotebookFileName left, NotebookFileName right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Value;
        }
    }
}
