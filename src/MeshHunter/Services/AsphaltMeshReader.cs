using System.Buffers.Binary;
using MeshHunter.Models;

namespace MeshHunter.Services;

public static class AsphaltMeshReader
{
    private const int MaxFileSize = 512 * 1024 * 1024;
    private const int MaxMaterialCount = 100;
    private const int MaxVertexCount = 2_000_000;
    private const int MaxIndexCount = 30_000_000;
    private const int LightAndTireDataSize = (6 * 3 + 4 * 3) * sizeof(short);

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
            throw new InvalidDataException("Asphalt binary OBJ exceeds the 512 MiB input safety limit.");
        return Read(File.ReadAllBytes(path));
    }

    public static ObjMesh Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileSize)
            throw new InvalidDataException("Asphalt binary OBJ exceeds the 512 MiB input safety limit.");
        if (data.Length < 5 + LightAndTireDataSize)
            throw new InvalidDataException("Asphalt binary OBJ is truncated.");

        var materialCount = data[0];
        if (materialCount == 0 || materialCount > MaxMaterialCount)
            throw new InvalidDataException("Asphalt binary OBJ has an invalid material count.");

        var vertexCountValue = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
        if (vertexCountValue < 3 || vertexCountValue > MaxVertexCount)
            throw new InvalidDataException("Asphalt binary OBJ has an invalid vertex count.");
        var vertexCount = (int)vertexCountValue;

        var verticesOffset = 5;
        var textureOffset = Advance(verticesOffset, (long)vertexCount * 3 * sizeof(short), data.Length);
        var normalFlagOffset = Advance(textureOffset, (long)vertexCount * 2, data.Length);
        if (normalFlagOffset >= data.Length)
            throw new InvalidDataException("Asphalt binary OBJ is missing its normal flag.");
        var hasNormals = data[normalFlagOffset];
        if (hasNormals > 1)
            throw new InvalidDataException("Asphalt binary OBJ has an invalid normal flag.");

        var position = normalFlagOffset + 1;
        if (hasNormals != 0)
            position = Advance(position, (long)vertexCount * 3 * sizeof(short), data.Length);

        var mesh = new ObjMesh();
        for (var index = 0; index < vertexCount; index++)
        {
            var vertexOffset = verticesOffset + index * 3 * sizeof(short);
            var x = BinaryPrimitives.ReadInt16LittleEndian(data[vertexOffset..]);
            var y = BinaryPrimitives.ReadInt16LittleEndian(data[(vertexOffset + 2)..]);
            var z = BinaryPrimitives.ReadInt16LittleEndian(data[(vertexOffset + 4)..]);
            mesh.Vertices.Add((x, y, z));
        }

        var totalIndexCount = 0;
        for (var materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            if (data.Length - position < sizeof(uint))
                throw new InvalidDataException("Asphalt binary OBJ has a truncated material index table.");

            var indexCountValue = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            position += sizeof(uint);
            if (indexCountValue > MaxIndexCount || indexCountValue % 3 != 0)
                throw new InvalidDataException("Asphalt binary OBJ has an invalid material index count.");

            var indexCount = (int)indexCountValue;
            if (indexCount > MaxIndexCount - totalIndexCount)
                throw new InvalidDataException("Asphalt binary OBJ has too many mesh indices.");
            position = Advance(position, (long)indexCount * sizeof(ushort), data.Length);
            totalIndexCount += indexCount;

            var indicesOffset = position - indexCount * sizeof(ushort);
            for (var index = 0; index < indexCount; index++)
            {
                var vertexIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    data[(indicesOffset + index * sizeof(ushort))..]);
                if (vertexIndex >= vertexCount)
                    throw new InvalidDataException("Asphalt binary OBJ references a vertex outside the mesh.");
                mesh.Triangles.Add(vertexIndex);
            }
        }

        if (totalIndexCount == 0 || data.Length - position != LightAndTireDataSize)
            throw new InvalidDataException("Asphalt binary OBJ has invalid trailing model data.");

        return mesh;
    }

    private static int Advance(int position, long length, int totalLength)
    {
        var next = (long)position + length;
        if (length < 0 || next > totalLength)
            throw new InvalidDataException("Asphalt binary OBJ section extends beyond the file.");
        return (int)next;
    }
}
