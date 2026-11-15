using MeshHunter.Models;

namespace MeshHunter.Services;

public static class RevoltPrmReader
{
    private const int IntermediateHeaderSize = 16;
    private const int PolygonRecordSize = 60;
    private const int IntermediatePolygonExtraSize = 8;
    private const int VertexRecordSize = 24;

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

    public static ObjMesh Read(string path, bool intermediate = false)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var headerSize = intermediate ? IntermediateHeaderSize : 0;
        if (intermediate)
        {
            var header = reader.ReadBytes(IntermediateHeaderSize);
            if (header.Length != IntermediateHeaderSize || header.Any(value => value != 0))
                throw new InvalidDataException("Invalid Re-Volt intermediate PRM header.");
        }

        var polygonCount = reader.ReadUInt16();
        var vertexCount = reader.ReadUInt16();
        if (polygonCount == 0 || vertexCount < 3)
            throw new InvalidDataException("Re-Volt PRM contains no usable mesh data.");

        var polygonRecordSize = PolygonRecordSize + (intermediate ? IntermediatePolygonExtraSize : 0);
        var requiredLength = headerSize + 4L + (long)polygonCount * polygonRecordSize + (long)vertexCount * VertexRecordSize;
        if (requiredLength > stream.Length)
            throw new InvalidDataException("Re-Volt PRM is truncated.");

        var mesh = new ObjMesh();
        var faces = new List<int[]>(polygonCount);
        for (var polygonIndex = 0; polygonIndex < polygonCount; polygonIndex++)
        {
            var type = reader.ReadUInt16();
            _ = reader.ReadInt16();
            var count = (type & 1) != 0 ? 4 : 3;
            var indices = new int[count];
            for (var i = 0; i < 4; i++)
            {
                var vertexIndex = reader.ReadUInt16();
                if (i < count)
                {
                    if (vertexIndex >= vertexCount)
                        throw new InvalidDataException("Re-Volt PRM polygon references a vertex outside the mesh.");
                    indices[i] = vertexIndex;
                }
            }

            stream.Seek(PolygonRecordSize - 12, SeekOrigin.Current);
            if (intermediate)
                stream.Seek(IntermediatePolygonExtraSize, SeekOrigin.Current);
            faces.Add(indices);
        }

        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            var x = reader.ReadSingle();
            var y = reader.ReadSingle();
            var z = reader.ReadSingle();
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException("Re-Volt PRM contains a non-finite vertex position.");

            mesh.Vertices.Add((x, y, z));
            stream.Seek(VertexRecordSize - 12, SeekOrigin.Current);
        }

        foreach (var face in faces)
        {
            for (var i = 1; i + 1 < face.Length; i++)
            {
                mesh.Triangles.Add(face[0]);
                mesh.Triangles.Add(face[i]);
                mesh.Triangles.Add(face[i + 1]);
            }
        }

        return mesh;
    }
}
