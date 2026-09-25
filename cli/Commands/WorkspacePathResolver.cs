using System;
using System.IO;

namespace MemoriaNote.Cli
{
    internal static class WorkspacePathResolver
    {
        internal static string Resolve(string workspaceOption)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var workspacePath = string.IsNullOrWhiteSpace(workspaceOption)
                ? currentDirectory
                : Path.GetFullPath(workspaceOption, currentDirectory);

            if (File.Exists(workspacePath))
            {
                throw new InvalidDataException(
                    $"The workspace path is not a directory: {workspacePath}");
            }
            if (!Directory.Exists(workspacePath))
            {
                throw new DirectoryNotFoundException(
                    $"The workspace directory does not exist: {workspacePath}");
            }

            return Path.GetFullPath(workspacePath);
        }
    }
}
