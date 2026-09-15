using System;

namespace MemoriaNote.Cli.Editors
{
    /// <summary>Represents immutable text exchanged with an external editor.</summary>
    public sealed class ExternalEditorDocument
    {
        /// <summary>Initializes an external editor document.</summary>
        /// <param name="fileName">The display file name used for the temporary file.</param>
        /// <param name="text">The initial text.</param>
        public ExternalEditorDocument(string fileName, string text)
        {
            FileName = fileName;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>Gets the display file name used for the temporary file.</summary>
        public string FileName { get; }

        /// <summary>Gets the initial text.</summary>
        public string Text { get; }
    }

    /// <summary>Represents the text returned by an external editor.</summary>
    public sealed class ExternalEditorResult
    {
        ExternalEditorResult(bool isChanged, string text)
        {
            IsChanged = isChanged;
            Text = text;
        }

        /// <summary>Gets whether the editor changed the document.</summary>
        public bool IsChanged { get; }

        /// <summary>Gets the resulting text.</summary>
        public string Text { get; }

        /// <summary>Creates a result for a changed document.</summary>
        /// <param name="text">The changed text.</param>
        /// <returns>A changed result.</returns>
        public static ExternalEditorResult Changed(string text)
        {
            return new ExternalEditorResult(
                true,
                text ?? throw new ArgumentNullException(nameof(text)));
        }

        /// <summary>Creates a result for an unchanged document.</summary>
        /// <param name="text">The original text.</param>
        /// <returns>An unchanged result.</returns>
        public static ExternalEditorResult Unchanged(string text)
        {
            return new ExternalEditorResult(
                false,
                text ?? throw new ArgumentNullException(nameof(text)));
        }
    }
}
