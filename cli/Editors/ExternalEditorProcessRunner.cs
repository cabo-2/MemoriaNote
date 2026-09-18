using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MemoriaNote.Cli.Editors
{
    internal interface IExternalEditorProcessRunner
    {
        Task RunAsync(
            string executablePath,
            string documentPath,
            CancellationToken cancellationToken);
    }

    internal interface IEditorProcess : IDisposable
    {
        bool HasExited { get; }

        int ExitCode { get; }

        Task WaitForExitAsync(CancellationToken cancellationToken);

        void Kill(bool entireProcessTree);
    }

    internal sealed class ExternalEditorProcessException : ExternalEditorException
    {
        internal ExternalEditorProcessException(string executablePath, int exitCode)
            : base($"External editor '{executablePath}' exited with code {exitCode}.")
        {
            ExitCode = exitCode;
        }

        internal int ExitCode { get; }
    }

    internal sealed class ExternalEditorProcessRunner : IExternalEditorProcessRunner
    {
        readonly ILogger<ExternalEditorProcessRunner> _logger;
        readonly Func<ProcessStartInfo, IEditorProcess> _startProcess;

        internal ExternalEditorProcessRunner(
            ILogger<ExternalEditorProcessRunner> logger)
            : this(logger, StartProcess)
        {
        }

        internal ExternalEditorProcessRunner(
            ILogger<ExternalEditorProcessRunner> logger,
            Func<ProcessStartInfo, IEditorProcess> startProcess)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _startProcess = startProcess ??
                throw new ArgumentNullException(nameof(startProcess));
        }

        public async Task RunAsync(
            string executablePath,
            string documentPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEditorProcess process;
            try
            {
                process = _startProcess(CreateStartInfo(
                    executablePath,
                    documentPath)) ?? throw new ExternalEditorStartException(executablePath);
            }
            catch (Exception exception) when (
                exception is Win32Exception ||
                exception is FileNotFoundException ||
                exception is DirectoryNotFoundException)
            {
                throw new ExternalEditorStartException(executablePath, exception);
            }

            using (process)
            {
                await WaitForExitAsync(process, executablePath, cancellationToken);
            }
        }

        async Task WaitForExitAsync(
            IEditorProcess process,
            string executablePath,
            CancellationToken cancellationToken)
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new ExternalEditorProcessException(
                    executablePath,
                    process.ExitCode);
            }
        }

        internal static ProcessStartInfo CreateStartInfo(
            string executablePath,
            string documentPath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException(
                    "An external editor executable is required.",
                    nameof(executablePath));
            if (string.IsNullOrWhiteSpace(documentPath))
                throw new ArgumentException(
                    "A document path is required.",
                    nameof(documentPath));

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(documentPath);
            return startInfo;
        }

        void TryKillProcessTree(IEditorProcess process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to terminate the external editor process tree.");
            }
        }

        static IEditorProcess StartProcess(ProcessStartInfo startInfo)
        {
            var process = Process.Start(startInfo) ??
                throw new ExternalEditorStartException(startInfo.FileName);
            return new SystemEditorProcess(process);
        }

        sealed class SystemEditorProcess : IEditorProcess
        {
            readonly Process _process;

            internal SystemEditorProcess(Process process)
            {
                _process = process;
            }

            public bool HasExited => _process.HasExited;

            public int ExitCode => _process.ExitCode;

            public Task WaitForExitAsync(CancellationToken cancellationToken)
            {
                return _process.WaitForExitAsync(cancellationToken);
            }

            public void Kill(bool entireProcessTree)
            {
                _process.Kill(entireProcessTree);
            }

            public void Dispose()
            {
                _process.Dispose();
            }
        }
    }
}
