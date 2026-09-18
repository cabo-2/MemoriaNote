using System;

namespace MemoriaNote.Cli.Editors
{
    internal abstract class ExternalEditorException : Exception
    {
        protected ExternalEditorException(string message)
            : base(message)
        {
        }

        protected ExternalEditorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class ExternalEditorConfigurationException : ExternalEditorException
    {
        internal ExternalEditorConfigurationException(string message)
            : base(message)
        {
        }
    }

    internal sealed class ExternalEditorStartException : ExternalEditorException
    {
        internal ExternalEditorStartException(
            string executablePath,
            Exception innerException = null)
            : base(
                $"External editor '{executablePath}' could not be started.",
                innerException)
        {
        }
    }
}
