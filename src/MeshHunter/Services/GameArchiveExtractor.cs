using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MeshHunter.Services;

public readonly record struct GameArchiveExtractionSummary(
    string Format,
    int EntryCount,
    int ExtractedCount,
    int SkippedCount);

public static class GameArchiveExtractor
{
    private const int MaxArchiveEntries = 20_000;
    private const int MaxArchiveSize = 512 * 1024 * 1024;
    private const int MaxExpandedEntrySize = 256 * 1024 * 1024;
    private const long MaxExpandedArchiveSize = 2L * 1024 * 1024 * 1024;
    private static readonly byte[] VfsMagic = "FUFS"u8.ToArray();
    private static readonly byte[] PlzpMagic = "PLZP"u8.ToArray();
    private static readonly byte[] PiffMagic = "PIFF"u8.ToArray();
    private static readonly byte[] PxcMagic = [0x4D, 0x3C, 0x2B, 0x1A];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public static bool TryExtractAsphaltBar(
        string path,
        string destination,
        out GameArchiveExtractionSummary summary)
    {
        summary = default;
        var data = ReadArchive(path);
        if (data.Length < 16)
            return false;

        var directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var regionLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var dataStartLong = 8L + directoryLength;
        if (directoryLength == 0 || dataStartLong > data.Length ||
            regionLength != data.Length - dataStartLong)
            return false;

        var dataStart = checked((int)dataStartLong);
        var entries = new List<BarEntry>();
        var position = 8;
        while (position < dataStart)
        {
            var terminator = data.AsSpan(position, dataStart - position).IndexOf((byte)0);
            if (terminator < 0)
                throw new InvalidDataException("Asphalt BAR directory contains an unterminated path.");

            var pathLength = terminator;
            var metadataOffset = position + pathLength + 1;
            if (pathLength == 0 || metadataOffset > dataStart - 4)
                throw new InvalidDataException("Asphalt BAR directory contains an incomplete entry.");

            var name = Encoding.UTF8.GetString(data, position, pathLength);
            var flags = data[metadataOffset];
            if (flags != 0x80 && flags != 0)
                throw new InvalidDataException("Asphalt BAR contains an unsupported entry flag.");

            var offset = (data[metadataOffset + 1] << 16) |
                         (data[metadataOffset + 2] << 8) |
                         data[metadataOffset + 3];
            entries.Add(new(name, flags, offset));
            if (entries.Count > MaxArchiveEntries)
                throw new InvalidDataException("Asphalt BAR contains too many entries.");

            position = metadataOffset + 4;
        }

        ValidateOffsets(entries.Select(entry => entry.Offset), (int)regionLength, "Asphalt BAR");
        Directory.CreateDirectory(destination);
        long expandedTotal = 0;
        var extractedCount = 0;
        var skippedCount = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Flags != 0x80)
            {
                skippedCount++;
                continue;
            }

            var blockEnd = index + 1 < entries.Count ? entries[index + 1].Offset : (int)regionLength;
            var blockLength = blockEnd - entry.Offset;
            if (blockLength < 5)
                throw new InvalidDataException("Asphalt BAR contains a truncated compressed entry.");

            var block = data.AsSpan(dataStart + entry.Offset, blockLength);
            var expectedLength = BinaryPrimitives.ReadUInt32BigEndian(block);
            if (expectedLength == 0 || expectedLength > MaxExpandedEntrySize)
                throw new InvalidDataException("Asphalt BAR entry exceeds the supported expansion limit.");

            var payload = InflateZlib(
                block[4..],
                checked((int)expectedLength),
                ref expandedTotal,
                "Asphalt BAR");
            var outputName = CreateSafeName(index, entry.Name, GetExtensionFromName(entry.Name));
            File.WriteAllBytes(Path.Combine(destination, outputName), payload);
            extractedCount++;
        }

        summary = new("Asphalt 4 BAR", entries.Count, extractedCount, skippedCount);
        return true;
    }

    public static bool TryExtractRagingThunderVfs(
        string path,
        string destination,
        out GameArchiveExtractionSummary summary)
    {
        summary = default;
        var data = ReadArchive(path);
        if (!data.AsSpan().StartsWith(VfsMagic))
            return false;
        if (data.Length < 12)
            throw new InvalidDataException("Raging Thunder VFS header is truncated.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (count == 0 || count > MaxArchiveEntries)
            throw new InvalidDataException("Raging Thunder VFS has an invalid entry count.");

        var tableEndLong = 12L + (long)count * 12;
        if (tableEndLong > data.Length)
            throw new InvalidDataException("Raging Thunder VFS entry table is truncated.");

        var entries = new List<VfsEntry>(checked((int)count));
        var position = 12;
        for (var index = 0; index < count; index++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
            var metadata = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 8));
            if (offset > int.MaxValue || size > int.MaxValue)
                throw new InvalidDataException("Raging Thunder VFS entry exceeds the supported size limit.");

            entries.Add(new((int)offset, (int)size, metadata));
            position += 12;
        }

        var tableEnd = checked((int)tableEndLong);
        if (entries[0].Offset != tableEnd)
            throw new InvalidDataException("Raging Thunder VFS data does not follow its entry table.");

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var hasNext = index + 1 < entries.Count;
            var nextOffset = hasNext ? entries[index + 1].Offset : 0;
            if (entry.Offset < tableEnd || entry.Size == 0 ||
                (long)entry.Offset + entry.Size > data.Length ||
                hasNext && (nextOffset < entry.Offset || entry.Size != nextOffset - entry.Offset))
                throw new InvalidDataException("Raging Thunder VFS entry offsets are inconsistent.");
        }

        var plzpCount = entries.Count(entry =>
            data.AsSpan(entry.Offset, entry.Size).StartsWith(PlzpMagic));
        var isRagingThunder2 = plzpCount > entries.Count - plzpCount;
        Directory.CreateDirectory(destination);
        long expandedTotal = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var block = data.AsSpan(entry.Offset, entry.Size);
            byte[] payload;
            if (block.StartsWith(PlzpMagic))
            {
                if (block.Length < 12)
                    throw new InvalidDataException("Raging Thunder 2 PLZP entry is truncated.");

                var expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(block[8..]);
                if (expectedLength == 0 || expectedLength > MaxExpandedEntrySize ||
                    compressedLength != block.Length - 12)
                    throw new InvalidDataException("Raging Thunder 2 PLZP entry has invalid lengths.");

                payload = InflateZlib(
                    block[12..],
                    checked((int)expectedLength),
                    ref expandedTotal,
                    "Raging Thunder 2");
            }
            else if (IsZlibHeader(block))
            {
                payload = InflateZlib(
                    block,
                    null,
                    ref expandedTotal,
                    isRagingThunder2 ? "Raging Thunder 2" : "Raging Thunder 1");
            }
            else
            {
                payload = block.ToArray();
            }

            var extension = GetPayloadExtension(payload);
            File.WriteAllBytes(
                Path.Combine(destination, CreateSafeName(index, $"entry-{index:D5}", extension)),
                payload);
        }

        summary = new(
            isRagingThunder2 ? "Raging Thunder 2 FUFS" : "Raging Thunder 1 FUFS",
            entries.Count,
            entries.Count,
            0);
        return true;
    }

    public static bool TryExtractRagingThunderPlzp(
        string path,
        string destination,
        out GameArchiveExtractionSummary summary)
    {
        summary = default;
        if (new FileInfo(path).Length < PlzpMagic.Length)
            return false;
        using (var probe = File.OpenRead(path))
        {
            Span<byte> magic = stackalloc byte[4];
            probe.ReadExactly(magic);
            if (!magic.SequenceEqual(PlzpMagic))
                return false;
        }

        var data = ReadArchive(path);
        if (data.Length < 12)
            throw new InvalidDataException("Raging Thunder 2 PLZP entry is truncated.");

        var expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (expectedLength == 0 || expectedLength > MaxExpandedEntrySize ||
            compressedLength != data.Length - 12)
            throw new InvalidDataException("Raging Thunder 2 PLZP entry has invalid lengths.");

        long expandedTotal = 0;
        var payload = InflateZlib(data.AsSpan(12), checked((int)expectedLength), ref expandedTotal, "Raging Thunder 2");
        Directory.CreateDirectory(destination);
        var extension = GetPayloadExtension(payload);
        File.WriteAllBytes(Path.Combine(destination, CreateSafeName(0, "payload", extension)), payload);
        summary = new("Raging Thunder 2 PLZP", 1, 1, 0);
        return true;
    }

    private static byte[] ReadArchive(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxArchiveSize)
            throw new InvalidDataException("Game archive exceeds the 512 MiB input safety limit.");
        return File.ReadAllBytes(path);
    }

    private static byte[] InflateZlib(
        ReadOnlySpan<byte> compressed,
        int? expectedLength,
        ref long expandedTotal,
        string format)
    {
        if (compressed.IsEmpty)
            throw new InvalidDataException($"{format} contains an empty compressed block.");

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var output = new MemoryStream(expectedLength is > 0 and <= 4 * 1024 * 1024 ? expectedLength.Value : 0);
        var buffer = new byte[81920];
        var expandedLength = 0;
        int read;
        while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
        {
            expandedLength = checked(expandedLength + read);
            expandedTotal += read;
            if (expandedLength > MaxExpandedEntrySize || expandedTotal > MaxExpandedArchiveSize ||
                expectedLength.HasValue && expandedLength > expectedLength.Value)
                throw new InvalidDataException($"{format} entry exceeds the supported expansion limit.");
            output.Write(buffer, 0, read);
        }

        if (expectedLength.HasValue && expandedLength != expectedLength.Value)
            throw new InvalidDataException($"{format} entry does not match its declared expanded size.");
        if (input.Position != input.Length)
            throw new InvalidDataException($"{format} entry contains trailing compressed data.");

        return output.ToArray();
    }

    private static bool IsZlibHeader(ReadOnlySpan<byte> block)
    {
        if (block.Length < 2)
            return false;
        var compressionMethod = block[0] & 0x0F;
        var windowSize = block[0] >> 4;
        var headerValue = (block[0] << 8) | block[1];
        return compressionMethod == 8 && windowSize <= 7 && headerValue % 31 == 0;
    }

    private static void ValidateOffsets(IEnumerable<int> offsets, int dataLength, string format)
    {
        var previous = -1;
        foreach (var offset in offsets)
        {
            if (offset <= previous || offset >= dataLength)
                throw new InvalidDataException($"{format} entry offsets are out of range or unsorted.");
            previous = offset;
        }
    }

    private static string GetPayloadExtension(ReadOnlySpan<byte> payload)
    {
        if (payload.StartsWith(PiffMagic)) return ".piff";
        if (payload.StartsWith(PxcMagic)) return ".pxc";
        if (payload.StartsWith(PngMagic)) return ".png";
        if (payload.StartsWith("DDS "u8)) return ".dds";
        if (payload.StartsWith(JpegMagic)) return ".jpg";
        return ".bin";
    }

    private static string GetExtensionFromName(string name) =>
        Path.GetExtension(name.Replace('\\', Path.DirectorySeparatorChar));

    private static string CreateSafeName(int index, string sourceName, string extension)
    {
        var basename = Path.GetFileName(sourceName.Replace('\\', '/'));
        var safeCharacters = basename
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .Where(character => character != '/' && character != '\\' && character != ':')
            .ToArray();
        var safeName = new string(safeCharacters);
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
            safeName = "entry";
        if (string.IsNullOrEmpty(Path.GetExtension(safeName)) && !string.IsNullOrEmpty(extension))
            safeName += extension;
        return $"{index:D5}_{safeName}";
    }

    private readonly record struct BarEntry(string Name, byte Flags, int Offset);
    private readonly record struct VfsEntry(int Offset, int Size, uint Metadata);
}
