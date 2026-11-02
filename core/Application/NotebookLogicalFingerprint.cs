using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MemoriaNote.Application;

/// <summary>Builds a stable logical-order fingerprint of authoritative notebook values.</summary>
internal sealed class NotebookLogicalFingerprintBuilder
{
    private readonly FingerprintSet _metadata = new("metadata");
    private readonly FingerprintSet _pages = new("pages");

    internal void Add(CurrentNotebookMetadataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _metadata.Add(record.Key, writer =>
        {
            writer.WriteString(record.Key);
            writer.WriteNullableString(record.Value);
        });
    }

    internal void Add(CurrentNotebookPageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _pages.Add(record.PageId, writer =>
        {
            writer.WriteString(record.PageId);
            writer.WriteString(record.Name);
            writer.WriteString(record.CreateTime);
            writer.WriteString(record.UpdateTime);
            writer.WriteInt64(record.Revision);
            writer.WriteString(record.Tags);
            writer.WriteString(record.Text);
        });
    }

    internal NotebookLogicalFingerprint Build() => new(
        _metadata.Count,
        _pages.Count,
        _metadata.Finish(),
        _pages.Finish());

    private sealed class FingerprintSet
    {
        private readonly List<FingerprintEntry> _entries = new();
        private readonly string _domain;

        internal FingerprintSet(string domain)
        {
            _domain = domain;
        }

        internal long Count => _entries.Count;

        internal void Add(string sortKey, Action<FingerprintRecordWriter> write)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var writer = new FingerprintRecordWriter(hash);
            writer.WriteString("memoria-note-logical-fingerprint-v1");
            writer.WriteString(_domain);
            write(writer);
            var recordHash = hash.GetHashAndReset();
            _entries.Add(new FingerprintEntry(sortKey, recordHash));
        }

        internal byte[] Finish()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var writer = new FingerprintRecordWriter(hash);
            writer.WriteString("memoria-note-logical-fingerprint-v1-result");
            writer.WriteString(_domain);
            writer.WriteInt64(Count);
            foreach (var entry in _entries
                         .OrderBy(candidate => candidate.SortKey, StringComparer.Ordinal))
            {
                writer.WriteString(entry.SortKey);
                writer.WriteBytes(entry.Digest);
            }
            return hash.GetHashAndReset();
        }

        private sealed record FingerprintEntry(string SortKey, byte[] Digest);
    }

    private sealed class FingerprintRecordWriter
    {
        private readonly IncrementalHash _hash;

        internal FingerprintRecordWriter(IncrementalHash hash)
        {
            _hash = hash;
        }

        internal void WriteNullableString(string value)
        {
            WriteBytes(value is null ? [0] : [1]);
            if (value is not null)
                WriteString(value);
        }

        internal void WriteString(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WriteBytes(Encoding.UTF8.GetBytes(value));
        }

        internal void WriteInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        internal void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            Span<byte> length = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
            _hash.AppendData(length);
            _hash.AppendData(bytes);
        }
    }
}

/// <summary>Identifies authoritative notebook values without exposing their contents.</summary>
internal sealed record NotebookLogicalFingerprint(
    long MetadataCount,
    long PageCount,
    byte[] MetadataDigest,
    byte[] PageDigest)
{
    internal bool Matches(NotebookLogicalFingerprint other) =>
        other is not null &&
        MetadataCount == other.MetadataCount &&
        PageCount == other.PageCount &&
        MetadataDigest.AsSpan().SequenceEqual(other.MetadataDigest) &&
        PageDigest.AsSpan().SequenceEqual(other.PageDigest);
}
