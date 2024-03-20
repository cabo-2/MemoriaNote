using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MemoriaNote.Cli.Editors
{
    public static class TerminalEditorFactory
    {
        static ProcessStartInfo LaunchShellCommand(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new ProcessStartInfo()
                {
                    FileName = "start",
                    Arguments = $"\"{filePath}\""
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                       RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return new ProcessStartInfo()
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\""
                };
            }
            else
            {
                return new ProcessStartInfo()
                {
                    FileName = "xdg-open", // or gnome-open   
                    Arguments = $"\"file://{filePath}\""
                };
            }
        }

        static ProcessStartInfo ExecuteCommand(string exec, string filePath)
                         => new ProcessStartInfo() { FileName = $"{exec}", Arguments = $"\"{filePath}\"" };

        public static ITerminalEditor Create()
        {
            if (ConfigurationCli.Instance.Terminal.EditorEnv)
            {
                var envs = Environment.GetEnvironmentVariables();
                if (envs.Contains(ConfigurationCli.TerminalSetting.EditorEnvName))
                    return new TerminalEditor((string)envs[ConfigurationCli.TerminalSetting.EditorEnvName]);
            }

            if (string.IsNullOrWhiteSpace(ConfigurationCli.Instance.Terminal.EditorPath))
                throw new ApplicationException("editor path");

            return new TerminalEditor(ConfigurationCli.Instance.Terminal.EditorPath);
        }
    }
}