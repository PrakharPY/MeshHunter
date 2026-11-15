using System.Buffers.Binary;
using System.Text;
using MeshHunter.Models;

namespace MeshHunter.Services;

public static class RagingThunderPxcMeshReader
{
    private const int MaxFileSize = 512 * 1024 * 1024;
    private const int MaxObjects = 100_000;
    private const int MaxDepth = 64;
    private const int MaxVertexCount = 2_000_000;
    private const int MaxIndexCount = 30_000_000;
    private const uint Magic = 0x1A2B3C4D;

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
            throw new InvalidDataException("Raging Thunder PXC exceeds the 512 MiB input safety limit.");
        return Read(File.ReadAllBytes(path));
    }

    public static ObjMesh Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileSize)
            throw new InvalidDataException("Raging Thunder PXC exceeds the 512 MiB input safety limit.");
        var parser = new Parser(data);
        return parser.Read();
    }

    private ref struct Parser
    {
        private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
        {
            "SGGR", "SGNO", "SGPC"
        };

        private static readonly HashSet<string> PlacedObjects = new(StringComparer.Ordinal)
        {
            "SGNO", "SGPS", "SGSP", "SGCA", "SGME"
        };

        private readonly ReadOnlySpan<byte> _data;
        private readonly List<SceneObject> _objects = [];
        private int _totalVertices;
        private int _totalIndices;

        public Parser(ReadOnlySpan<byte> data)
        {
            _data = data;
        }

        public ObjMesh Read()
        {
            if (_data.Length < 8 ||
                BinaryPrimitives.ReadUInt32LittleEndian(_data) != Magic ||
                BinaryPrimitives.ReadUInt16LittleEndian(_data[4..]) != 19 ||
                BinaryPrimitives.ReadUInt16LittleEndian(_data[6..]) != 1)
            {
                throw new InvalidDataException("Unsupported Raging Thunder PXC header or version.");
            }

            var end = ReadObject(8, _data.Length, -1, 0);
            if (end != _data.Length)
                throw new InvalidDataException("Raging Thunder PXC contains trailing bytes.");

            ApplyWorldTransforms();
            return BuildMesh();
        }

        private int ReadObject(int offset, int parentEnd, int parent, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidDataException("Raging Thunder PXC object tree is too deeply nested.");

            var chunk = ReadChunk(offset, parentEnd);
            var (_, position) = ReadString(chunk.Data, chunk.End);
            var objectData = Advance(position, 4, chunk.End);
            if (_objects.Count >= MaxObjects)
                throw new InvalidDataException("Raging Thunder PXC contains too many scene objects.");

            var objectIndex = _objects.Count;
            var sceneObject = new SceneObject(parent);
            _objects.Add(sceneObject);

            if (Containers.Contains(chunk.Tag))
            {
                var childCount = ReadCount(objectData, chunk.End, MaxObjects, "scene child");
                objectData = Advance(objectData, 4, chunk.End);
                for (var index = 0; index < childCount; index++)
                    objectData = ReadObject(objectData, chunk.End, objectIndex, depth + 1);
            }

            if (PlacedObjects.Contains(chunk.Tag))
            {
                if (objectData > chunk.End - 76)
                    throw new InvalidDataException("Raging Thunder PXC placement data is truncated.");

                sceneObject.Matrix = new double[9];
                for (var index = 0; index < sceneObject.Matrix.Length; index++)
                    sceneObject.Matrix[index] = ReadFixed(objectData + 28 + index * 4);

                sceneObject.Translation = new double[3];
                for (var index = 0; index < sceneObject.Translation.Length; index++)
                    sceneObject.Translation[index] = ReadFixed(objectData + 64 + index * 4);

                objectData = Advance(objectData, 76, chunk.End);
                objectData = ReadChunk(objectData, chunk.End).End;
                if (chunk.Tag == "SGPS")
                {
                    var payload = ReadChunk(objectData, chunk.End);
                    if (payload.Tag == "MESH")
                        sceneObject.Mesh = ReadMesh(payload.Data, payload.End);
                    objectData = payload.End;
                }
            }

            return chunk.End;
        }

        private MeshData ReadMesh(int offset, int end)
        {
            var position = Advance(offset, 28, end);
            var format = ReadUInt32(position, end);
            var vertexCount = ReadCount(position + 4, end, MaxVertexCount, "vertex");
            if (vertexCount < 3)
                throw new InvalidDataException("Raging Thunder PXC mesh has too few vertices.");
            if (format is not (0x1000 or 0x1010 or 0x1100 or 0x1110) ||
                ReadUInt32(position + 8, end) != 0)
            {
                throw new InvalidDataException("Raging Thunder PXC mesh uses an unsupported vertex layout.");
            }

            if (vertexCount > MaxVertexCount - _totalVertices)
                throw new InvalidDataException("Raging Thunder PXC contains too many vertices.");
            _totalVertices += vertexCount;

            var stride = 20 + ((format & 0x10) != 0 ? 12 : 0) + ((format & 0x100) != 0 ? 4 : 0);
            var vertexOffset = Advance(position, 12, end);
            position = Advance(vertexOffset, (long)vertexCount * stride, end);

            var primitiveType = ReadUInt32(position, end);
            var indexCount = ReadCount(position + 4, end, MaxIndexCount, "mesh index");
            var indexType = ReadUInt32(position + 8, end);
            if (primitiveType != 4 || indexType != 5 || indexCount == 0 || indexCount % 3 != 0)
                throw new InvalidDataException("Raging Thunder PXC mesh does not use a valid GL_TRIANGLES ushort index block.");

            if (indexCount > MaxIndexCount - _totalIndices)
                throw new InvalidDataException("Raging Thunder PXC contains too many mesh indices.");
            _totalIndices += indexCount;

            var indexOffset = Advance(position, 12, end);
            position = Advance(indexOffset, (long)indexCount * sizeof(ushort), end);
            for (var index = 0; index < indexCount; index++)
            {
                if (ReadUInt16(indexOffset + index * sizeof(ushort), end) >= vertexCount)
                    throw new InvalidDataException("Raging Thunder PXC mesh index is outside its vertex block.");
            }

            var materialCount = ReadCount(position, end, ushort.MaxValue, "material");
            position = Advance(position, 4, end);
            for (var index = 0; index < materialCount; index++)
            {
                var texture = ReadChunk(position, end);
                if (texture.Tag == "TEXT")
                    _ = ReadString(Advance(texture.Data, 4, texture.End), texture.End);
                position = Advance(texture.End, 48, end);
            }

            var rangeCount = ReadCount(position, end, MaxIndexCount / 3, "draw range");
            position = Advance(position, 4, end);
            var rangesOffset = position;
            position = Advance(position, (long)rangeCount * 8, end);

            var ranges = new List<DrawRange>(rangeCount);
            for (var index = 0; index < rangeCount; index++)
            {
                var range = rangesOffset + index * 8;
                var material = ReadUInt16(range + 2, end);
                var count = ReadUInt16(range + 4, end);
                var first = ReadUInt16(range + 6, end);
                if (material >= materialCount || count == 0 || count % 3 != 0 ||
                    first % 3 != 0 || (long)first + count > indexCount)
                {
                    throw new InvalidDataException("Raging Thunder PXC draw range is outside its mesh.");
                }
                ranges.Add(new(material, first, count));
            }

            return new(vertexOffset, vertexCount, stride, indexOffset, ranges);
        }

        private void ApplyWorldTransforms()
        {
            for (var index = 0; index < _objects.Count; index++)
            {
                var sceneObject = _objects[index];
                var localMatrix = sceneObject.Matrix ?? IdentityMatrix();
                var localTranslation = sceneObject.Translation ?? new double[3];
                if (sceneObject.Parent < 0)
                {
                    sceneObject.WorldMatrix = localMatrix;
                    sceneObject.WorldTranslation = localTranslation;
                    continue;
                }

                var parent = _objects[sceneObject.Parent];
                var worldMatrix = new double[9];
                var worldTranslation = new double[3];
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        worldMatrix[row * 3 + column] =
                            parent.WorldMatrix[row * 3] * localMatrix[column] +
                            parent.WorldMatrix[row * 3 + 1] * localMatrix[3 + column] +
                            parent.WorldMatrix[row * 3 + 2] * localMatrix[6 + column];
                    }

                    worldTranslation[row] =
                        parent.WorldMatrix[row * 3] * localTranslation[0] +
                        parent.WorldMatrix[row * 3 + 1] * localTranslation[1] +
                        parent.WorldMatrix[row * 3 + 2] * localTranslation[2] +
                        parent.WorldTranslation[row];
                }

                sceneObject.WorldMatrix = worldMatrix;
                sceneObject.WorldTranslation = worldTranslation;
            }
        }

        private ObjMesh BuildMesh()
        {
            var mesh = new ObjMesh();
            foreach (var sceneObject in _objects)
            {
                if (sceneObject.Mesh is not { } source)
                    continue;

                var vertexBase = mesh.Vertices.Count;
                for (var vertex = 0; vertex < source.VertexCount; vertex++)
                {
                    var offset = source.VertexOffset + vertex * source.Stride;
                    var x = ReadFixed(offset);
                    var y = ReadFixed(offset + 4);
                    var z = ReadFixed(offset + 8);
                    var matrix = sceneObject.WorldMatrix;
                    var translation = sceneObject.WorldTranslation;
                    mesh.Vertices.Add((
                        matrix[0] * x + matrix[1] * y + matrix[2] * z + translation[0],
                        matrix[3] * x + matrix[4] * y + matrix[5] * z + translation[1],
                        matrix[6] * x + matrix[7] * y + matrix[8] * z + translation[2]));
                }

                foreach (var range in source.Ranges)
                {
                    for (var index = range.First; index < range.First + range.Count; index++)
                    {
                        var vertex = ReadUInt16(source.IndexOffset + index * sizeof(ushort), _data.Length);
                        mesh.Triangles.Add(vertexBase + vertex);
                    }
                }
            }

            if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
                throw new InvalidDataException("Raging Thunder PXC contains no supported model geometry.");
            return mesh;
        }

        private (string Text, int Next) ReadString(int offset, int end)
        {
            var length = ReadUInt16(offset, end);
            var textStart = Advance(offset, 2, end);
            var next = Advance(textStart, length, end);
            if (length == 0 || _data[next - 1] != 0)
                throw new InvalidDataException("Raging Thunder PXC string is not null-terminated.");

            return (Encoding.Latin1.GetString(_data.Slice(textStart, length - 1)), next);
        }

        private (string Tag, int Data, int End) ReadChunk(int offset, int parentEnd)
        {
            var headerEnd = Advance(offset, 8, parentEnd);
            var length = ReadUInt32(offset + 4, parentEnd);
            var end = Advance(headerEnd, length, parentEnd);
            return (Encoding.Latin1.GetString(_data.Slice(offset, 4)), headerEnd, end);
        }

        private int ReadCount(int offset, int end, int maximum, string kind)
        {
            var count = ReadUInt32(offset, end);
            if (count > maximum)
                throw new InvalidDataException($"Raging Thunder PXC {kind} count exceeds the supported limit.");
            return (int)count;
        }

        private uint ReadUInt32(int offset, int end)
        {
            _ = Advance(offset, sizeof(uint), end);
            return BinaryPrimitives.ReadUInt32LittleEndian(_data[offset..]);
        }

        private ushort ReadUInt16(int offset, int end)
        {
            _ = Advance(offset, sizeof(ushort), end);
            return BinaryPrimitives.ReadUInt16LittleEndian(_data[offset..]);
        }

        private double ReadFixed(int offset)
        {
            _ = Advance(offset, sizeof(int), _data.Length);
            return BinaryPrimitives.ReadInt32LittleEndian(_data[offset..]) / 65536.0;
        }

        private static int Advance(int offset, long count, int end)
        {
            var next = offset + count;
            if (offset < 0 || count < 0 || next > end || next > int.MaxValue)
                throw new InvalidDataException("Raging Thunder PXC structure extends beyond its containing block.");
            return (int)next;
        }

        private static double[] IdentityMatrix() => [1, 0, 0, 0, 1, 0, 0, 0, 1];

        private sealed class SceneObject(int parent)
        {
            public int Parent { get; } = parent;
            public double[]? Matrix { get; set; }
            public double[]? Translation { get; set; }
            public double[] WorldMatrix { get; set; } = IdentityMatrix();
            public double[] WorldTranslation { get; set; } = new double[3];
            public MeshData? Mesh { get; set; }
        }

        private sealed record MeshData(
            int VertexOffset,
            int VertexCount,
            int Stride,
            int IndexOffset,
            List<DrawRange> Ranges);

        private readonly record struct DrawRange(int Material, int First, int Count);
    }
}
