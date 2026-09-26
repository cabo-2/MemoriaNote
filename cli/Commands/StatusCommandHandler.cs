using System;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Cli
{
    internal sealed class StatusCommandHandler
    {
        const string Unavailable = "(unavailable)";
        const string NotSelected = "(not selected)";

        readonly CliCommandExecutor _executor;
        readonly WorkspaceStatusUseCase _status;
        readonly ICommandOutput _output;

        internal StatusCommandHandler(
            CliCommandExecutor executor,
            WorkspaceStatusUseCase status,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var workspacePath = WorkspacePathResolver.Resolve(workspaceOption);
                var status = await _status
                    .InspectAsync(workspacePath, token)
                    .ConfigureAwait(false);
                Write(status);
                return ToCommandResult(status);
            }, cancellationToken);
        }

        void Write(WorkspaceStatusResult status)
        {
            _output.WriteLine($"Workspace: {status.WorkspacePath}");
            _output.WriteLine($"Location:  {status.VirtualLocation ?? Unavailable}");
            _output.WriteLine(
                $"Notebook:  {status.CurrentNotebook?.Value ?? NotebookDisplay(status)}");
            _output.WriteLine($"Status:    {StatusDisplay(status.Status)}");
            if (status.Detail != null)
                _output.WriteLine($"Detail:    {status.Detail}");

            var next = NextAction(status);
            if (next != null)
                _output.WriteLine($"Next:      {next}");
        }

        static CliCommandResult ToCommandResult(WorkspaceStatusResult status)
        {
            return status.Status switch
            {
                WorkspaceStatusKind.Missing => CliCommandResult.Failure(
                    CliErrorKind.NotFound,
                    status.Detail,
                    alreadyReported: true),
                WorkspaceStatusKind.Invalid or WorkspaceStatusKind.Unsupported =>
                    CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        status.Detail,
                        alreadyReported: true),
                _ => CliCommandResult.Success()
            };
        }

        static string NotebookDisplay(WorkspaceStatusResult status)
        {
            return status.Status == WorkspaceStatusKind.Root
                ? NotSelected
                : Unavailable;
        }

        static string StatusDisplay(WorkspaceStatusKind status)
        {
            return status switch
            {
                WorkspaceStatusKind.Root => "root",
                WorkspaceStatusKind.Ready => "ready",
                WorkspaceStatusKind.Missing => "missing",
                WorkspaceStatusKind.Invalid => "invalid",
                WorkspaceStatusKind.Unsupported => "unsupported",
                WorkspaceStatusKind.ReadOnly => "read-only",
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        static string NextAction(WorkspaceStatusResult status)
        {
            if (status.Status == WorkspaceStatusKind.Root)
                return "Run 'mn use <notebook>' to select a notebook.";
            if (status.Subject == WorkspaceStatusSubject.Notebook &&
                (status.Status == WorkspaceStatusKind.Missing ||
                    status.Status == WorkspaceStatusKind.Invalid ||
                    status.Status == WorkspaceStatusKind.Unsupported))
            {
                return "Run 'mn use <notebook>' or 'mn use --root'.";
            }
            if (status.Subject == WorkspaceStatusSubject.Workspace &&
                status.Status == WorkspaceStatusKind.Unsupported)
            {
                return "Upgrade Memoria Note, or use '--notebook <notebook>' " +
                    "for one invocation.";
            }
            if (status.Subject == WorkspaceStatusSubject.Workspace &&
                status.Status == WorkspaceStatusKind.Invalid)
            {
                return "Fix 'mn-workspace.toml', or use '--notebook <notebook>' " +
                    "for one invocation.";
            }

            return null;
        }
    }
}
