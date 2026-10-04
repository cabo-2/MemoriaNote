using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoriaNote.Models;
using MemoriaNote.Persistence;

namespace MemoriaNote.Transfer
{
    /// <summary>
    /// Exports notebook pages as text files.
    /// </summary>
    public sealed class TextPageExporter
    {
        const string PageIdSuffixMarker = "~id~";

        static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        readonly INotebookTransferRepository _transferRepository;
        readonly PageFileNameCodec _fileNameCodec;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextPageExporter"/> class.
        /// </summary>
        /// <param name="transferRepository">The repository used to read exported pages.</param>
        public TextPageExporter(INotebookTransferRepository transferRepository)
            : this(transferRepository, new PageFileNameCodec())
        {
        }

        /// <summary>
        /// Initializes a text page exporter with an explicit file-name codec.
        /// </summary>
        /// <param name="transferRepository">The repository used to read exported pages.</param>
        /// <param name="fileNameCodec">The codec used for file and directory names.</param>
        public TextPageExporter(
            INotebookTransferRepository transferRepository,
            PageFileNameCodec fileNameCodec)
        {
            _transferRepository = transferRepository ??
                throw new ArgumentNullException(nameof(transferRepository));
            _fileNameCodec = fileNameCodec ??
                throw new ArgumentNullException(nameof(fileNameCodec));
        }

        /// <summary>
        /// Exports every page from a notebook and atomically publishes a new directory.
        /// </summary>
        /// <param name="request">The source, destination, and name-conflict policy.</param>
        /// <param name="token">The cancellation token for the operation.</param>
        /// <returns>The classified export result.</returns>
        public async Task<TextPageExportResult> ExportAsync(
            TextPageExportRequest request,
            CancellationToken token)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            token.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(request.DestinationDirectory);
            if (PathExists(destination))
            {
                return TextPageExportResult.Failed(
                    TextPageExportErrorCode.DestinationConflict);
            }

            var pages = await _transferRepository.ListPagesAsync(
                    request.NotebookId,
                    token)
                .ConfigureAwait(false);

            var parentDirectory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parentDirectory))
                throw new InvalidOperationException("The export destination has no parent directory.");

            string temporaryDirectory = null;
            try
            {
                temporaryDirectory = CreateTemporaryDirectory(parentDirectory);
                var plan = await CreatePlanAsync(
                        pages,
                        temporaryDirectory,
                        request.NameConflictPolicy,
                        token)
                    .ConfigureAwait(false);
                if (plan == null)
                {
                    return TextPageExportResult.Failed(
                        TextPageExportErrorCode.NameConflict);
                }

                foreach (var item in plan)
                {
                    token.ThrowIfCancellationRequested();
                    var path = Path.Combine(temporaryDirectory, item.FileName);
                    try
                    {
                        await WriteTextAsync(path, item.Page.Text ?? string.Empty, token)
                            .ConfigureAwait(false);
                    }
                    catch (PathTooLongException)
                    {
                        return TextPageExportResult.Failed(
                            TextPageExportErrorCode.NameConflict);
                    }
                    catch (IOException) when (File.Exists(path))
                    {
                        return TextPageExportResult.Failed(
                            TextPageExportErrorCode.NameConflict);
                    }
                }

                token.ThrowIfCancellationRequested();
                try
                {
                    Directory.Move(temporaryDirectory, destination);
                    temporaryDirectory = null;
                }
                catch (IOException) when (PathExists(destination))
                {
                    return TextPageExportResult.Failed(
                        TextPageExportErrorCode.DestinationConflict);
                }

                return TextPageExportResult.Succeeded(plan.Count);
            }
            catch (UnauthorizedAccessException)
            {
                return TextPageExportResult.Failed(TextPageExportErrorCode.IoFailure);
            }
            catch (IOException)
            {
                return TextPageExportResult.Failed(TextPageExportErrorCode.IoFailure);
            }
            finally
            {
                if (temporaryDirectory != null)
                    TryDeleteDirectory(temporaryDirectory);
            }
        }

        async Task<IReadOnlyList<ExportPlanItem>> CreatePlanAsync(
            IReadOnlyList<Page> pages,
            string temporaryDirectory,
            TextPageExportNameConflictPolicy conflictPolicy,
            CancellationToken token)
        {
            var items = pages
                .Select(page => new ExportPlanItem(
                    page,
                    _fileNameCodec.Encode(page.Name) + ".txt"))
                .ToList();
            var collidedItems = new HashSet<ExportPlanItem>();
            var probes = new List<ExportPlanItem>();
            var parkingDirectory = Path.Combine(temporaryDirectory, ".mn-probe-parking");
            Directory.CreateDirectory(parkingDirectory);

            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(temporaryDirectory, item.FileName);
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    probes.Add(item);
                }
                catch (PathTooLongException)
                {
                    return null;
                }
                catch (IOException) when (File.Exists(path))
                {
                    var partner = FindCollisionPartner(
                        item,
                        probes,
                        temporaryDirectory,
                        parkingDirectory);
                    if (partner == null)
                        return null;

                    collidedItems.Add(partner);
                    collidedItems.Add(item);
                }
            }

            foreach (var probe in probes)
                File.Delete(Path.Combine(temporaryDirectory, probe.FileName));
            Directory.Delete(parkingDirectory);

            if (collidedItems.Count == 0)
                return items.AsReadOnly();
            if (conflictPolicy == TextPageExportNameConflictPolicy.Fail)
                return null;

            var suffixedPlan = items
                .Select(item => collidedItems.Contains(item)
                    ? item.WithFileName(AddPageIdSuffix(item))
                    : item)
                .ToList();
            return await ValidateFinalPlanAsync(
                    suffixedPlan,
                    temporaryDirectory,
                    token)
                .ConfigureAwait(false)
                ? suffixedPlan.AsReadOnly()
                : null;
        }

        static ExportPlanItem FindCollisionPartner(
            ExportPlanItem candidate,
            IReadOnlyList<ExportPlanItem> probes,
            string temporaryDirectory,
            string parkingDirectory)
        {
            var candidatePath = Path.Combine(temporaryDirectory, candidate.FileName);
            foreach (var probe in probes)
            {
                var probePath = Path.Combine(temporaryDirectory, probe.FileName);
                var parkedPath = Path.Combine(
                    parkingDirectory,
                    Guid.NewGuid().ToString("N"));
                File.Move(probePath, parkedPath);
                var isPartner = !File.Exists(candidatePath);
                File.Move(parkedPath, probePath);
                if (isPartner)
                    return probe;
            }

            return null;
        }

        static string AddPageIdSuffix(ExportPlanItem item)
        {
            if (item.Page.Guid == Guid.Empty)
                return item.FileName;

            var stem = item.FileName.Substring(0, item.FileName.Length - 4);
            return stem + PageIdSuffixMarker +
                item.Page.Guid.ToString("D").ToLowerInvariant() + ".txt";
        }

        static async Task<bool> ValidateFinalPlanAsync(
            IReadOnlyList<ExportPlanItem> plan,
            string temporaryDirectory,
            CancellationToken token)
        {
            var paths = new List<string>(plan.Count);
            try
            {
                foreach (var item in plan)
                {
                    token.ThrowIfCancellationRequested();
                    var path = Path.Combine(temporaryDirectory, item.FileName);
                    try
                    {
                        using var stream = new FileStream(
                            path,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 1,
                            useAsync: true);
                        await stream.FlushAsync(token).ConfigureAwait(false);
                        paths.Add(path);
                    }
                    catch (PathTooLongException)
                    {
                        return false;
                    }
                    catch (IOException) when (File.Exists(path))
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                foreach (var path in paths)
                    File.Delete(path);
            }
        }

        static async Task WriteTextAsync(
            string path,
            string text,
            CancellationToken token)
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(
                stream,
                Utf8WithoutBom,
                bufferSize: 4096,
                leaveOpen: false);
            await writer.WriteAsync(text.AsMemory(), token).ConfigureAwait(false);
        }

        static string CreateTemporaryDirectory(string parentDirectory)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var path = Path.Combine(
                    parentDirectory,
                    ".mn-export-" + Guid.NewGuid().ToString("N"));
                if (PathExists(path))
                    continue;

                Directory.CreateDirectory(path);
                return path;
            }

            throw new IOException("Could not create a unique export working directory.");
        }

        static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        sealed class ExportPlanItem
        {
            internal ExportPlanItem(Page page, string fileName)
            {
                Page = page;
                FileName = fileName;
            }

            internal Page Page { get; }

            internal string FileName { get; }

            internal ExportPlanItem WithFileName(string fileName)
            {
                return new ExportPlanItem(Page, fileName);
            }
        }
    }
}
