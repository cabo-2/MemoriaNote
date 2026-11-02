using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MemoriaNote.Application;

namespace MemoriaNote.Archive;

/// <summary>Adapts a prevalidated CM2 stream to the common current-record pull boundary.</summary>
internal sealed class ArchiveV1ContentModel2TransferBridge
{
    private readonly Stream _archive;
    private readonly ArchiveV1ContentModel2Reader _reader;
    private readonly IReadOnlyList<CurrentNotebookMetadataRecord> _metadata;

    private ArchiveV1ContentModel2TransferBridge(
        Stream archive,
        ArchiveV1ContentModel2Reader reader,
        IReadOnlyList<CurrentNotebookMetadataRecord> metadata,
        int pageCount)
    {
        _archive = archive;
        _reader = reader;
        _metadata = metadata;
        PageCount = pageCount;
    }

    internal int MetadataCount => _metadata.Count;

    internal int PageCount { get; }

    internal CurrentNotebookTransfer CreateTransfer(CancellationToken cancellationToken) =>
        new(_metadata, ReadPagesAsync(cancellationToken));

    internal static async Task<ArchiveV1ContentModel2TransferBridge> PrepareAsync(
        Stream archive,
        ArchiveV1ContentModel2Reader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(reader);
        var counter = new CountingSink();
        var report = await reader.ReadPrevalidatedAsync(archive, counter, cancellationToken)
            .ConfigureAwait(false);
        if (!report.IsValid)
            throw new InvalidDataException("The prevalidated CM2 archive changed before restore.");
        return new ArchiveV1ContentModel2TransferBridge(
            archive,
            reader,
            counter.Metadata.AsReadOnly(),
            counter.PageCount);
    }

    private async IAsyncEnumerable<CurrentNotebookPageRecord> ReadPagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<CurrentNotebookPageRecord>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var producer = ProduceAsync(channel.Writer, cancellationToken);
        await foreach (var page in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return page;
        await producer.ConfigureAwait(false);
    }

    private async Task ProduceAsync(
        ChannelWriter<CurrentNotebookPageRecord> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await _reader.ReadPrevalidatedAsync(
                    _archive,
                    new ChannelSink(writer),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!report.IsValid)
                throw new InvalidDataException("The prevalidated CM2 archive changed during restore.");
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            throw;
        }
    }

    private sealed class CountingSink : IArchiveV1ContentModel2RecordSink
    {
        internal List<CurrentNotebookMetadataRecord> Metadata { get; } = new();

        internal int PageCount { get; private set; }

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken)
        {
            Metadata.Add(new CurrentNotebookMetadataRecord(row.Key, row.Value));
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePageAsync(
            ArchiveV1ContentModel2PageRow row,
            CancellationToken cancellationToken)
        {
            PageCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ChannelSink : IArchiveV1ContentModel2RecordSink
    {
        private readonly ChannelWriter<CurrentNotebookPageRecord> _writer;

        internal ChannelSink(ChannelWriter<CurrentNotebookPageRecord> writer)
        {
            _writer = writer;
        }

        public ValueTask WriteMetadataAsync(
            ArchiveV1MetadataRow row,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask WritePageAsync(
            ArchiveV1ContentModel2PageRow row,
            CancellationToken cancellationToken) =>
            _writer.WriteAsync(
                new CurrentNotebookPageRecord(
                    row.PageId,
                    row.Name,
                    row.CreateTime,
                    row.UpdateTime,
                    row.Revision,
                    row.Tags,
                    row.Text),
                cancellationToken);
    }
}
