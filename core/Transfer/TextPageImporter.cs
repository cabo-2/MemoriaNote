using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Application;
using MemoriaNote.Persistence;

namespace MemoriaNote.Transfer
{
    /// <summary>Imports flat strict-UTF-8 text files as one atomic notebook operation.</summary>
    public sealed class TextPageImporter
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        readonly ITextPageImportRepository _repository;
        readonly PageFileNameCodec _fileNameCodec;
        readonly PageValidationPolicy _validationPolicy;

        /// <summary>Initializes a text page importer.</summary>
        public TextPageImporter(ITextPageImportRepository repository)
            : this(repository, new PageFileNameCodec(), new PageValidationPolicy())
        {
        }

        /// <summary>Initializes a text page importer with explicit validation collaborators.</summary>
        public TextPageImporter(
            ITextPageImportRepository repository,
            PageFileNameCodec fileNameCodec,
            PageValidationPolicy validationPolicy)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _fileNameCodec = fileNameCodec ??
                throw new ArgumentNullException(nameof(fileNameCodec));
            _validationPolicy = validationPolicy ??
                throw new ArgumentNullException(nameof(validationPolicy));
        }

        /// <summary>Preflights and atomically imports text files from a directory.</summary>
        public async Task<TextPageImportResult> ImportAsync(
            TextPageImportRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(request.SourceDirectory))
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.InputNotFound);
            }

            try
            {
                var sources = EnumerateSources(request.SourceDirectory);
                var items = new List<TextPageImportItem>(sources.Count);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    var name = _fileNameCodec.Decode(
                        source.Name.Substring(0, source.Name.Length - 4));
                    if (_validationPolicy.ValidateName(name).Count > 0)
                    {
                        return TextPageImportResult.Failed(
                            TextPageImportErrorCode.InvalidPageName);
                    }
                    if (!names.Add(name))
                    {
                        return TextPageImportResult.Failed(
                            TextPageImportErrorCode.DuplicateInputName);
                    }

                    var bytes = await File.ReadAllBytesAsync(source.FullName, token)
                        .ConfigureAwait(false);
                    var content = bytes.AsSpan();
                    if (content.StartsWith(Encoding.UTF8.Preamble))
                        content = content.Slice(Encoding.UTF8.Preamble.Length);
                    var text = StrictUtf8.GetString(content);
                    items.Add(new TextPageImportItem(name, text));
                }

                return await _repository.ImportAsync(
                        request.NotebookId,
                        items,
                        request.ConflictPolicy,
                        request.DryRun,
                        token)
                    .ConfigureAwait(false);
            }
            catch (DecoderFallbackException)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.InvalidEncoding);
            }
            catch (FileNotFoundException)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.InputNotFound);
            }
            catch (DirectoryNotFoundException)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.InputNotFound);
            }
            catch (UnauthorizedAccessException)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return TextPageImportResult.Failed(
                    TextPageImportErrorCode.IoFailure);
            }
        }

        static IReadOnlyList<FileInfo> EnumerateSources(string sourceDirectory)
        {
            return new DirectoryInfo(sourceDirectory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file =>
                    file.Name.EndsWith(".txt", StringComparison.Ordinal) &&
                    (file.Attributes & (FileAttributes.Directory |
                        FileAttributes.ReparsePoint | FileAttributes.Device)) == 0)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }
    }
}
