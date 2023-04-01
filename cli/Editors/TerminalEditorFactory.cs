using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MemoriaNote.Cli.Editors
{
    public static class TerminalEditorFactory
    {
        //
        // Editor selected method
        // 
        // Windows:
        //   1. PATH
        //   2. winget
        //   3. Git for Windows
        //
        // Linux:        
        //   1. PATH
        //

        static string[] WindowsPathCommands = new string[] {
            "where.exe nano.exe",
            "where.exe vim.exe"
        };

        static string[] WindowsWingetCommands = new string[] {
            @"C:\Program Files\Git\usr\bin\nano.exe",
            @"C:\Program Files\Git\usr\bin\vim.exe"
        };

        static string[] WindowsGitCommands = new string[] {
            @"C:\Program Files\Git\usr\bin\nano.exe",
            @"C:\Program Files\Git\usr\bin\vim.exe"
        };

        static string[] LinuxPathCommands = new string[] {
            @"nano",
            @"vim"
        };

        public static bool SearchInstallEditor(out string execPath)
        {
            execPath = null;
            Console.WriteLine("Search install editor...");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach(var editor in WindowsGitCommands)
                {

                }
            }   
            else
            {
                foreach(var editor in LinuxPathCommands)
                {
                    var startInfo = new ProcessStartInfo() {
                        FileName = "which",
                        Arguments = editor
                    };
                    var proc = Process.Start(startInfo);
                    Console.Write($"{editor}...");
                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                    {
                        Console.WriteLine(" Found");
                        execPath = editor;
                        return true;
                    }
                    else
                    {
                        Console.WriteLine(" Not Found");
                    }
                }
            }  
            return false;     
        }

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
            return new TerminalEditor("nano");
        }
    }
}