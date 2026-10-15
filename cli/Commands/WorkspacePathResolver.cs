using System;
using System.IO;

namespace MemoriaNote.Cli
{
    internal static class WorkspacePathResolver
    {
        internal static string Resolve(string workspaceOption)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            string workspacePath;
            try
            {
                workspacePath = string.IsNullOrWhiteSpace(workspaceOption)
                    ? currentDirectory
                    : Path.GetFullPath(workspaceOption, currentDirectory);
            }
            catch (Exception exception) when (CliMessage.IsInvalidPathException(exception))
            {
                throw new CliValidationException(
                    "The workspace path is invalid.",
                    exception);
            }

            if (File.Exists(workspacePath))
            {
                throw new CliValidationException(
                    $"The workspace path is not a directory: " +
                    $"{CliMessage.Quote(workspacePath)}.");
            }
            if (!Directory.Exists(workspacePath))
            {
                throw new DirectoryNotFoundException(
                    $"The workspace directory does not exist: " +
                    $"{CliMessage.Quote(workspacePath)}.");
            }

            return Path.GetFullPath(workspacePath);
        }
    }
}
