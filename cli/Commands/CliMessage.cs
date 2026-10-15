using System;
using System.IO;

namespace MemoriaNote.Cli
{
    internal sealed class CliValidationException : Exception
    {
        internal CliValidationException(string message)
            : base(message)
        {
        }

        internal CliValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal static class CliMessage
    {
        internal const string NoSelectedNotebook =
            "No notebook is selected. Run 'mn use <notebook>' or specify " +
            "'--notebook <notebook>' for this invocation.";

        internal static string AsSentence(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("A message is required.", nameof(message));

            var trimmed = message.TrimEnd();
            var finalCharacter = trimmed[^1];
            return finalCharacter == '.' || finalCharacter == '!' || finalCharacter == '?'
                ? trimmed
                : trimmed + ".";
        }

        internal static string ArgumentReason(ArgumentException exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var message = exception.Message;
            if (string.IsNullOrEmpty(exception.ParamName))
                return AsSentence(message);

            var parameterMarker = $"(Parameter '{exception.ParamName}')";
            var markerIndex = message.LastIndexOf(
                parameterMarker,
                StringComparison.Ordinal);
            if (markerIndex >= 0)
                message = message.Substring(0, markerIndex).TrimEnd();

            return AsSentence(message);
        }

        internal static bool IsInvalidPathException(Exception exception)
        {
            return exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException;
        }

        internal static string Quote(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" + value
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
        }
    }
}
