using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MeshHunter.Models;

namespace MeshHunter.Services;

/// <summary>Writes a single-mesh glTF 2.0 file or binary GLB file.</summary>
public sealed class GltfExporter : IModelExporter
{
    private const int ArrayBufferTarget = 34962;
    private const int ElementArrayBufferTarget = 34963;
    private const int FloatComponentType = 5126;
    private const int UnsignedIntComponentType = 5125;

    public string Format => "glTF 2.0 / GLB";
    public string FileExtension => ".gltf";

    public void Export(ObjMesh mesh, string destinationPath)
    {
        if (Path.GetExtension(destinationPath).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            ExportGlb(mesh, destinationPath);
        else
            ExportGltf(mesh, destinationPath);
    }

    private static (byte[] Binary, float[] Min, float[] Max, int IndexOffset, int IndexLength) BuildBinary(ObjMesh mesh)
    {
        if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
            throw new InvalidDataException("Cannot export an empty mesh.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var min = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        var max = new[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        foreach (var (x, y, z) in mesh.Vertices)
        {
            var values = new[] { (float)x, (float)y, (float)z };
            for (var axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis], values[axis]);
                max[axis] = Math.Max(max[axis], values[axis]);
                writer.Write(values[axis]);
            }
        }

        var indexOffset = checked((int)stream.Position);
        while (stream.Position % 4 != 0) writer.Write((byte)0);
        indexOffset = checked((int)stream.Position);
        foreach (var index in mesh.Triangles)
        {
            if (index < 0 || index >= mesh.Vertices.Count) throw new InvalidDataException("Mesh contains an invalid triangle index.");
            writer.Write((uint)index);
        }
        writer.Flush();
        return (stream.ToArray(), min, max, indexOffset, checked(mesh.Triangles.Count * sizeof(uint)));
    }

    private static object CreateDocument(int vertexCount, int indexCount, int indexOffset, int binaryLength, float[] min, float[] max, string? bufferUri)
    {
        object buffer = bufferUri is null
            ? new { byteLength = binaryLength }
            : new { byteLength = binaryLength, uri = bufferUri };
        return new
        {
            asset = new { version = "2.0", generator = "MeshHunter" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0 }, indices = 1, mode = 4 } } } },
            buffers = new[] { buffer },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = vertexCount * 3 * sizeof(float), target = ArrayBufferTarget },
                new { buffer = 0, byteOffset = indexOffset, byteLength = indexCount * sizeof(uint), target = ElementArrayBufferTarget }
            },
            accessors = new object[]
            {
                new { bufferView = 0, byteOffset = 0, componentType = FloatComponentType, count = vertexCount, type = "VEC3", min, max },
                new { bufferView = 1, byteOffset = 0, componentType = UnsignedIntComponentType, count = indexCount, type = "SCALAR" }
            }
        };
    }

    private static void ExportGltf(ObjMesh mesh, string destinationPath)
    {
        var data = BuildBinary(mesh);
        var binPath = Path.ChangeExtension(destinationPath, ".bin");
        var document = CreateDocument(mesh.Vertices.Count, mesh.Triangles.Count, data.IndexOffset, data.Binary.Length, data.Min, data.Max, Path.GetFileName(binPath));
        File.WriteAllBytes(binPath, data.Binary);
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }), new UTF8Encoding(false));
    }

    private static void ExportGlb(ObjMesh mesh, string destinationPath)
    {
        var data = BuildBinary(mesh);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            CreateDocument(mesh.Vertices.Count, mesh.Triangles.Count, data.IndexOffset, data.Binary.Length, data.Min, data.Max, null),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var jsonLength = (json.Length + 3) & ~3;
        var binLength = (data.Binary.Length + 3) & ~3;
        var totalLength = checked(12 + 8 + jsonLength + 8 + binLength);

        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: false);
        writer.Write(0x46546C67); // glTF magic
        writer.Write(2);
        writer.Write(totalLength);
        writer.Write(jsonLength);
        writer.Write(0x4E4F534A); // JSON chunk
        writer.Write(json);
        for (var i = json.Length; i < jsonLength; i++) writer.Write((byte)0x20);
        writer.Write(binLength);
        writer.Write(0x004E4942); // BIN chunk
        writer.Write(data.Binary);
        for (var i = data.Binary.Length; i < binLength; i++) writer.Write((byte)0);
    }
}
