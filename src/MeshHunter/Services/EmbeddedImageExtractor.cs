using System.Buffers.Binary;

namespace MeshHunter.Services;

public static class EmbeddedImageExtractor
{
    private const int MaxContainerSize = 512 * 1024 * 1024;
    private const int MaxImageSize = 128 * 1024 * 1024;
    private const int MaxDimension = 32_768;
    private const long MaxPixelCount = 268_435_456;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly uint[] PngCrcTable = CreatePngCrcTable();

    public static bool TryExtract(string path, string destination, out int extractedCount)
    {
        extractedCount = 0;
        var sourceLength = new FileInfo(path).Length;
        if (sourceLength > MaxContainerSize)
            throw new InvalidDataException("Embedded image container exceeds the 512 MiB input safety limit.");

        var data = File.ReadAllBytes(path);
        var images = new List<ImageRange>();
        var searchOffset = 0;
        long totalImageSize = 0;
        while (searchOffset < data.Length)
        {
            var remainder = data.AsSpan(searchOffset);
            var pngOffset = remainder.IndexOf(PngSignature);
            var jpegOffset = remainder.IndexOf(JpegSignature);
            if (pngOffset < 0 && jpegOffset < 0)
                break;

            var relativeOffset = pngOffset < 0
                ? jpegOffset
                : jpegOffset < 0 ? pngOffset : Math.Min(pngOffset, jpegOffset);
            var imageOffset = searchOffset + relativeOffset;
            var isPng = pngOffset >= 0 && pngOffset == relativeOffset;
            var valid = isPng
                ? TryGetPngLength(data, imageOffset, out var imageLength)
                : TryGetJpegLength(data, imageOffset, out imageLength);
            if (!valid)
            {
                searchOffset = imageOffset + 1;
                continue;
            }

            if (imageLength > MaxImageSize)
                throw new InvalidDataException("Embedded image exceeds the 128 MiB extraction limit.");

            totalImageSize += imageLength;
            if (totalImageSize > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Embedded images exceed the 2 GiB extraction limit.");

            images.Add(new(imageOffset, imageLength, isPng ? ".png" : ".jpg"));
            searchOffset = imageOffset + imageLength;
        }

        if (images.Count < 2)
            return false;

        Directory.CreateDirectory(destination);
        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var fileName = $"image-{index:D5}{image.Extension}";
            File.WriteAllBytes(
                Path.Combine(destination, fileName),
                data.AsSpan(image.Offset, image.Length).ToArray());
        }

        extractedCount = images.Count;
        return true;
    }

    private static bool TryGetPngLength(byte[] data, int start, out int length)
    {
        length = 0;
        if (start < 0 || start > data.Length - PngSignature.Length ||
            !data.AsSpan(start).StartsWith(PngSignature))
            return false;

        var position = start + PngSignature.Length;
        var sawHeader = false;
        var sawImageData = false;
        while (position <= data.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            if (chunkLength > MaxImageSize || chunkLength > data.Length - position - 12)
                return false;

            var typeOffset = position + 4;
            var payloadOffset = position + 8;
            var payloadLength = (int)chunkLength;
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadOffset + payloadLength));
            if (ComputePngCrc(data.AsSpan(typeOffset, 4), data.AsSpan(payloadOffset, payloadLength)) != storedCrc)
                return false;

            var chunkType = data.AsSpan(typeOffset, 4);
            if (!sawHeader)
            {
                if (!chunkType.SequenceEqual("IHDR"u8) || payloadLength != 13)
                    return false;

                var width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadOffset));
                var height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadOffset + 4));
                var colorType = data[payloadOffset + 9];
                var bitDepth = data[payloadOffset + 8];
                if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension ||
                    (long)width * height > MaxPixelCount || data[payloadOffset + 10] != 0 ||
                    data[payloadOffset + 11] != 0 || data[payloadOffset + 12] > 1 ||
                    !IsValidPngBitDepth(colorType, bitDepth))
                    return false;
                sawHeader = true;
            }
            else if (chunkType.SequenceEqual("IHDR"u8))
            {
                return false;
            }

            if (chunkType.SequenceEqual("IDAT"u8))
                sawImageData = true;

            position = payloadOffset + payloadLength + 4;
            if (chunkType.SequenceEqual("IEND"u8))
            {
                if (payloadLength != 0 || !sawHeader || !sawImageData)
                    return false;
                length = position - start;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetJpegLength(byte[] data, int start, out int length)
    {
        length = 0;
        if (start < 0 || start > data.Length - 3 || !data.AsSpan(start).StartsWith(JpegSignature))
            return false;

        var position = start + 2;
        var sawFrame = false;
        var sawScan = false;
        var inScan = false;
        while (position < data.Length && position - start <= MaxImageSize)
        {
            if (inScan)
            {
                var markerOffset = data.AsSpan(position).IndexOf((byte)0xFF);
                if (markerOffset < 0)
                    return false;
                var markerStart = position + markerOffset;
                var markerByteOffset = markerStart + 1;
                while (markerByteOffset < data.Length && data[markerByteOffset] == 0xFF)
                    markerByteOffset++;
                if (markerByteOffset >= data.Length)
                    return false;

                var scanMarker = data[markerByteOffset];
                if (scanMarker == 0x00 || scanMarker is >= 0xD0 and <= 0xD7)
                {
                    position = markerByteOffset + 1;
                    continue;
                }

                position = markerStart;
                inScan = false;
            }

            if (data[position] != 0xFF)
                return false;
            while (position < data.Length && data[position] == 0xFF)
                position++;
            if (position >= data.Length)
                return false;

            var marker = data[position++];
            if (marker == 0xD9)
            {
                if (!sawFrame || !sawScan)
                    return false;
                length = position - start;
                return true;
            }

            if (marker == 0x00)
                return false;
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
                continue;
            if (position > data.Length - 2)
                return false;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
            if (segmentLength < 2 || segmentLength > data.Length - position)
                return false;

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 8)
                    return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 3));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 5));
                if (width == 0 || height == 0 || (long)width * height > MaxPixelCount)
                    return false;
                sawFrame = true;
            }

            position += segmentLength;
            if (marker == 0xDA)
            {
                sawScan = true;
                inScan = true;
            }
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    private static bool IsValidPngBitDepth(byte colorType, byte bitDepth) =>
        colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };

    private static uint ComputePngCrc(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> payload)
    {
        var crc = uint.MaxValue;
        foreach (var value in chunkType)
            crc = PngCrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (var value in payload)
            crc = PngCrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreatePngCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var crc = (uint)index;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            table[index] = crc;
        }

        return table;
    }

    private readonly record struct ImageRange(int Offset, int Length, string Extension);
}
