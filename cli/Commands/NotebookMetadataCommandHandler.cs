using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Domain;
using MemoriaNote.Models;

namespace MemoriaNote.Cli
{
    internal sealed class NotebookMetadataCommandHandler
    {
        readonly CliCommandExecutor _executor;
        readonly INotebookTargetSessionResolver _targetResolver;
        readonly INotebookMetadataService _metadataService;
        readonly ICommandInput _input;
        readonly ICommandOutput _output;

        internal NotebookMetadataCommandHandler(
            CliCommandExecutor executor,
            INotebookTargetSessionResolver targetResolver,
            INotebookMetadataService metadataService,
            ICommandInput input,
            ICommandOutput output)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
            _metadataService = metadataService ??
                throw new ArgumentNullException(nameof(metadataService));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        internal Task<int> ExecuteAsync(
            string workspaceOption,
            string notebookOption,
            string name,
            string title,
            string description,
            string author,
            string tag,
            string readOnly,
            CancellationToken cancellationToken)
        {
            return _executor.ExecuteAsync(async token =>
            {
                var options = CreateSpecifiedOptions(
                    name,
                    title,
                    description,
                    author,
                    tag,
                    readOnly);
                if (options.Length > 1)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Specify only one metadata update option at a time.");
                }

                if (readOnly != null && readOnly != "true" && readOnly != "false")
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Read-only must be true or false.");
                }

                if (options.Length == 1 &&
                    options[0].Value == "-" &&
                    options[0].Field != NotebookMetadataField.ReadOnly &&
                    _input.IsInteractive)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        "Redirect or pipe standard input when a metadata value is '-'.");
                }

                var session = await _targetResolver.ResolveAsync(
                        workspaceOption,
                        notebookOption,
                        token)
                    .ConfigureAwait(false);
                var notebookPath = session.Workspace.SelectedNotebook.DatabasePath;
                var notebookId = NotebookId.FromDatabasePath(notebookPath);
                var notebookFileName = Path.GetFileName(notebookPath);

                if (options.Length == 0)
                {
                    var loaded = await _metadataService
                        .GetAsync(notebookId, token)
                        .ConfigureAwait(false);
                    if (loaded.HasIssues)
                    {
                        return CliCommandResult.Failure(
                            CliErrorKind.Validation,
                            "The notebook metadata is invalid.");
                    }

                    WriteMetadata(notebookFileName, loaded.Metadata);
                    return CliCommandResult.Success();
                }

                var option = options[0];
                var value = option.Value;
                if (value == "-" && option.Field != NotebookMetadataField.ReadOnly)
                {
                    value = await _input.ReadToEndAsync(token).ConfigureAwait(false);
                    value = RemoveOneTerminalLineEnding(value);
                }

                var result = await _metadataService
                    .UpdateAsync(
                        new NotebookMetadataUpdateRequest(notebookId, option.Field, value),
                        token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return CliCommandResult.Failure(
                        CliErrorKind.Validation,
                        NotebookMetadataValidationMessageMapper.ToErrorMessage(
                            result.Errors[0]));
                }

                var fieldName = ToOptionName(option.Field);
                _output.WriteLine(
                    result.Changed
                        ? $"Metadata updated: notebook={notebookFileName}, field={fieldName}"
                        : $"Metadata unchanged: notebook={notebookFileName}");
                return CliCommandResult.Success();
            }, cancellationToken);
        }

        void WriteMetadata(string notebookFileName, NotebookMetadata metadata)
        {
            _output.WriteLine($"Notebook: {Escape(notebookFileName)}");
            _output.WriteLine($"Name: {Escape(metadata.Name)}");
            _output.WriteLine($"Title: {Escape(metadata.Title)}");
            _output.WriteLine($"Description: {Escape(metadata.Description)}");
            _output.WriteLine($"Author: {Escape(metadata.Author)}");
            _output.WriteLine($"Tag: {Escape(metadata.Tag)}");
            _output.WriteLine($"ReadOnly: {metadata.ReadOnly.ToString().ToLowerInvariant()}");
            _output.WriteLine($"Version: {Escape(metadata.Version)}");
            _output.WriteLine(
                "CreateTime: " +
                (metadata.CreateTime == default
                    ? "(unset)"
                    : metadata.CreateTime.ToString(
                        "yyyy-MM-dd'T'HH:mm:ss",
                        CultureInfo.InvariantCulture)));
        }

        static MetadataOption[] CreateSpecifiedOptions(
            string name,
            string title,
            string description,
            string author,
            string tag,
            string readOnly)
        {
            var options = new System.Collections.Generic.List<MetadataOption>();
            Add(options, NotebookMetadataField.Name, name);
            Add(options, NotebookMetadataField.Title, title);
            Add(options, NotebookMetadataField.Description, description);
            Add(options, NotebookMetadataField.Author, author);
            Add(options, NotebookMetadataField.Tag, tag);
            Add(options, NotebookMetadataField.ReadOnly, readOnly);
            return options.ToArray();
        }

        static void Add(
            System.Collections.Generic.ICollection<MetadataOption> options,
            NotebookMetadataField field,
            string value)
        {
            if (value != null)
                options.Add(new MetadataOption(field, value));
        }

        static string RemoveOneTerminalLineEnding(string value)
        {
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 2);
            if (value.EndsWith("\n", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 1);

            return value;
        }

        static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                        {
                            escaped.Append("\\u");
                            escaped.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }
                        break;
                }
            }

            return escaped.ToString();
        }

        static string ToOptionName(NotebookMetadataField field)
        {
            return field switch
            {
                NotebookMetadataField.Name => "name",
                NotebookMetadataField.Title => "title",
                NotebookMetadataField.Description => "description",
                NotebookMetadataField.Author => "author",
                NotebookMetadataField.Tag => "tag",
                NotebookMetadataField.ReadOnly => "read-only",
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
        }

        readonly struct MetadataOption
        {
            internal MetadataOption(NotebookMetadataField field, string value)
            {
                Field = field;
                Value = value;
            }

            internal NotebookMetadataField Field { get; }

            internal string Value { get; }
        }
    }
}
