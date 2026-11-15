using System.Buffers.Binary;
using MeshHunter.Models;

namespace MeshHunter.Services;

public static class PiffMeshReader
{
    private const int MaxFileSize = 512 * 1024 * 1024;
    private const int MaxVertexCount = 2_000_000;
    private const int MaxIndexCount = 30_000_000;
    private static ReadOnlySpan<byte> Magic => "PIFF"u8;
    private static ReadOnlySpan<byte> HeaderTag => "3DOBHEAD"u8;

    public static bool TryRead(string path, out ObjMesh mesh)
    {
        try
        {
            mesh = Read(path);
            return true;
        }
        catch (InvalidDataException)
        {
            mesh = new ObjMesh();
            return false;
        }
        catch (EndOfStreamException)
        {
            mesh = new ObjMesh();
            return false;
        }
    }

    public static ObjMesh Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileSize)
            throw new InvalidDataException("PIFF file exceeds the 512 MiB input safety limit.");
        return Read(File.ReadAllBytes(path));
    }

    public static ObjMesh Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileSize)
            throw new InvalidDataException("PIFF file exceeds the 512 MiB input safety limit.");
        if (data.Length < 20 || !data.StartsWith(Magic))
            throw new InvalidDataException("Invalid PIFF file signature.");

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (declaredLength != data.Length - 8)
            throw new InvalidDataException("PIFF file length does not match its header.");

        var position = 8;
        if (!data[position..].StartsWith(HeaderTag))
            throw new InvalidDataException("PIFF 3D object header is missing.");

        var headerLength = ReadChunkLength(data, position + 8);
        position = AdvanceChunk(position, 12, headerLength, data.Length);

        var meshOffset = -1;
        var meshLength = 0;
        var polygonChunks = new List<(int Offset, int Length)>();
        while (position < data.Length)
        {
            if (data.Length - position < 8)
                throw new InvalidDataException("PIFF contains a truncated chunk header.");

            var chunkTag = data.Slice(position, 4);
            var chunkLength = ReadChunkLength(data, position + 4);
            var chunkDataOffset = position + 8;
            if (chunkTag.SequenceEqual("MESH"u8))
            {
                if (meshOffset >= 0)
                    throw new InvalidDataException("PIFF contains multiple mesh position chunks.");
                meshOffset = chunkDataOffset;
                meshLength = chunkLength;
            }
            else if (chunkTag.SequenceEqual("POLY"u8))
            {
                polygonChunks.Add((chunkDataOffset, chunkLength));
            }

            position = AdvanceChunk(position, 8, chunkLength, data.Length);
        }

        if (meshOffset < 0 || meshLength == 0 || meshLength % 12 != 0 || polygonChunks.Count == 0)
            throw new InvalidDataException("PIFF contains no supported mesh geometry.");

        var vertexCount = meshLength / 12;
        if (vertexCount < 3 || vertexCount > MaxVertexCount)
            throw new InvalidDataException("PIFF vertex count is outside the supported range.");

        var mesh = new ObjMesh();
        for (var index = 0; index < vertexCount; index++)
        {
            var vertexOffset = meshOffset + index * 12;
            var x = ReadFixedPoint(data, vertexOffset);
            var y = ReadFixedPoint(data, vertexOffset + 4);
            var z = ReadFixedPoint(data, vertexOffset + 8);
            mesh.Vertices.Add((x, y, z));
        }

        var totalIndexCount = 0;
        foreach (var (offset, length) in polygonChunks)
        {
            if (length < 16)
                throw new InvalidDataException("PIFF polygon chunk is truncated.");

            var polygon = data.Slice(offset, length);
            var primitiveType = BinaryPrimitives.ReadUInt32LittleEndian(polygon);
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(polygon[4..]);
            var indexType = BinaryPrimitives.ReadUInt32LittleEndian(polygon[12..]);
            var indexWidth = indexType switch
            {
                0x1401 => 1,
                0x1403 => 2,
                _ => 0
            };

            if (primitiveType != 4 || indexWidth == 0 || indexCount == 0 ||
                indexCount > MaxIndexCount || indexCount % 3 != 0 ||
                (long)indexCount * indexWidth != length - 16)
                throw new InvalidDataException("PIFF polygon index data uses an unsupported layout.");

            totalIndexCount = checked(totalIndexCount + (int)indexCount);
            if (totalIndexCount > MaxIndexCount)
                throw new InvalidDataException("PIFF contains too many mesh indices.");

            for (var index = 0; index < indexCount; index++)
            {
                var vertexIndex = indexWidth == 1
                    ? polygon[16 + (int)index]
                    : BinaryPrimitives.ReadUInt16LittleEndian(polygon[(16 + (int)index * 2)..]);
                if (vertexIndex >= vertexCount)
                    throw new InvalidDataException("PIFF polygon references a vertex outside the mesh.");
                mesh.Triangles.Add(vertexIndex);
            }
        }

        if (mesh.Triangles.Count == 0)
            throw new InvalidDataException("PIFF contains no usable triangles.");

        return mesh;
    }

    private static int ReadChunkLength(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
            throw new InvalidDataException("PIFF contains a truncated chunk length.");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        if (length > int.MaxValue)
            throw new InvalidDataException("PIFF chunk exceeds the supported size limit.");
        return (int)length;
    }

    private static int AdvanceChunk(int position, int headerLength, int chunkLength, int totalLength)
    {
        var next = (long)position + headerLength + chunkLength;
        if (next > totalLength)
            throw new InvalidDataException("PIFF chunk extends beyond the file.");
        return (int)next;
    }

    private static double ReadFixedPoint(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]) / 65536.0;
}
