using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MeshHunter.Models;
using MeshHunter.Services;
using Xunit;

namespace MeshHunter.Tests;

public sealed class GameFormatReaderTests
{
    [Fact]
    public void ReadsPiffMeshesWithByteAndWordIndices()
    {
        var mesh = PiffMeshReader.Read(CreatePiff());

        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal((1d, 0d, 0d), mesh.Vertices[1]);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, mesh.Triangles);
    }

    [Fact]
    public void RejectsPiffIndicesOutsideTheVertexArray()
    {
        var data = CreatePiff(byteIndices: [0, 1, 4]);

        Assert.Throws<InvalidDataException>(() => PiffMeshReader.Read(data));
    }

    [Fact]
    public void ReadsRagingThunderPxcModelThroughTheMeshImportPath()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "sample.pxc");
        File.WriteAllBytes(path, CreateRagingThunderPxc());

        var mesh = MeshFormatReader.Read(path);

        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal((1d, 0d, 0d), mesh.Vertices[1]);
        Assert.Equal(new[] { 0, 1, 2 }, mesh.Triangles);
    }

    [Fact]
    public void RejectsRagingThunderPxcIndicesOutsideTheVertexArray()
    {
        var data = CreateRagingThunderPxc(invalidIndex: true);

        Assert.Throws<InvalidDataException>(() => RagingThunderPxcMeshReader.Read(data));
    }

    [Fact]
    public void ReadsAsphaltBinaryObjThroughTheMeshImportPath()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "sample.obj");
        File.WriteAllBytes(path, CreateAsphaltBinaryObj());

        var mesh = MeshFormatReader.Read(path);

        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal((100d, 0d, 0d), mesh.Vertices[1]);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, mesh.Triangles);
    }

    [Fact]
    public void RejectsAsphaltBinaryObjIndicesOutsideTheVertexArray()
    {
        var data = CreateAsphaltBinaryObj(invalidIndex: true);

        Assert.Throws<InvalidDataException>(() => AsphaltMeshReader.Read(data));
    }

    [Fact]
    public void ExtractsRagingThunderOneVfsAndImportsItsPiffMesh()
    {
        using var temp = new TemporaryDirectory();
        var archivePath = Path.Combine(temp.Path, "sample.vfs");
        File.WriteAllBytes(archivePath, CreateVfs(Compress(CreatePiff())));

        var results = new AssetAnalyzer().Analyze([archivePath], temp.Path);

        Assert.Contains(results, asset => asset.Format == "Raging Thunder 1 FUFS");
        Assert.Contains(results, asset => asset.Kind == AssetKind.Mesh && asset.Format == "PIFF");
    }

    [Fact]
    public void ExtractsRagingThunderTwoPlzpPayload()
    {
        using var temp = new TemporaryDirectory();
        var payload = Encoding.ASCII.GetBytes("DDS ");
        var archivePath = Path.Combine(temp.Path, "sample.vfs");
        File.WriteAllBytes(archivePath, CreateVfs(CreatePlzp(payload)));
        var destination = Path.Combine(temp.Path, "extracted");

        Assert.True(GameArchiveExtractor.TryExtractRagingThunderVfs(archivePath, destination, out var summary));

        Assert.Equal("Raging Thunder 2 FUFS", summary.Format);
        Assert.Equal(1, summary.ExtractedCount);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(destination, "00000_entry-00000.dds")));
    }

    [Fact]
    public void ExtractsOnlyValidatedCompressedAsphaltBarEntries()
    {
        using var temp = new TemporaryDirectory();
        var archivePath = Path.Combine(temp.Path, "carsb.bar");
        var destination = Path.Combine(temp.Path, "extracted");
        File.WriteAllBytes(archivePath, CreateBar());

        Assert.True(GameArchiveExtractor.TryExtractAsphaltBar(archivePath, destination, out var summary));

        Assert.Equal("Asphalt 4 BAR", summary.Format);
        Assert.Equal(2, summary.EntryCount);
        Assert.Equal(1, summary.ExtractedCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal("opaque mesh payload", File.ReadAllText(Path.Combine(destination, "00000_sample.obj")));
    }

    [Fact]
    public void ExtractsEmbeddedPngAndJpegImages()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "textures");
        var destination = Path.Combine(temp.Path, "images");
        var collection = new byte[] { 0x06, 0x03, 0x03, 0x03, 0x03, 0x03 };
        File.WriteAllBytes(source, collection.Concat(CreatePng()).Concat(CreateJpeg()).ToArray());

        Assert.True(EmbeddedImageExtractor.TryExtract(source, destination, out var count));

        Assert.Equal(2, count);
        Assert.True(File.Exists(Path.Combine(destination, "image-00000.png")));
        Assert.True(File.Exists(Path.Combine(destination, "image-00001.jpg")));
    }

    [Fact]
    public void DoesNotExtractPngWithAnInvalidChunkCrc()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "textures");
        var destination = Path.Combine(temp.Path, "images");
        var invalidPng = CreatePng();
        invalidPng[^1] ^= 0xFF;
        File.WriteAllBytes(source, invalidPng.Concat(CreatePng()).ToArray());

        Assert.False(EmbeddedImageExtractor.TryExtract(source, destination, out var count));

        Assert.Equal(0, count);
        Assert.False(Directory.Exists(destination));
    }

    private static byte[] CreatePiff(byte[]? byteIndices = null)
    {
        byteIndices ??= [0, 1, 2];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("PIFF"));
        writer.Write(0u);
        writer.Write(Encoding.ASCII.GetBytes("3DOBHEAD"));
        writer.Write(0u);

        using var vertices = new MemoryStream();
        using (var vertexWriter = new BinaryWriter(vertices, Encoding.ASCII, leaveOpen: true))
        {
            foreach (var vertex in new[]
            {
                (X: 0, Y: 0, Z: 0),
                (X: 65536, Y: 0, Z: 0),
                (X: 0, Y: 65536, Z: 0),
                (X: 0, Y: 0, Z: 65536)
            })
            {
                vertexWriter.Write(vertex.X);
                vertexWriter.Write(vertex.Y);
                vertexWriter.Write(vertex.Z);
            }
        }

        WriteChunk(writer, "MESH", vertices.ToArray());
        WriteChunk(writer, "POLY", CreatePolygon(0x1401, byteIndices));
        WriteChunk(writer, "POLY", CreatePolygon(0x1403, [0, 2, 3]));
        writer.Flush();

        var result = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static byte[] CreateRagingThunderPxc(bool invalidIndex = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0x1A2B3C4Du);
        writer.Write((ushort)19);
        writer.Write((ushort)1);

        using var meshData = new MemoryStream();
        using (var meshWriter = new BinaryWriter(meshData, Encoding.ASCII, leaveOpen: true))
        {
            meshWriter.Write(new byte[28]);
            meshWriter.Write(0x1000u);
            meshWriter.Write(3u);
            meshWriter.Write(0u);
            foreach (var vertex in new[]
            {
                (X: 0, Y: 0, Z: 0),
                (X: 65536, Y: 0, Z: 0),
                (X: 0, Y: 65536, Z: 0)
            })
            {
                meshWriter.Write(vertex.X);
                meshWriter.Write(vertex.Y);
                meshWriter.Write(vertex.Z);
                meshWriter.Write(new byte[8]);
            }

            meshWriter.Write(4u);
            meshWriter.Write(3u);
            meshWriter.Write(5u);
            meshWriter.Write((ushort)0);
            meshWriter.Write((ushort)1);
            meshWriter.Write(invalidIndex ? (ushort)3 : (ushort)2);
            meshWriter.Write(1u);
            WriteChunk(meshWriter, "NONE", []);
            meshWriter.Write(new byte[48]);
            meshWriter.Write(1u);
            meshWriter.Write((ushort)0);
            meshWriter.Write((ushort)0);
            meshWriter.Write((ushort)3);
            meshWriter.Write((ushort)0);
        }

        using var sceneObject = new MemoryStream();
        using (var objectWriter = new BinaryWriter(sceneObject, Encoding.ASCII, leaveOpen: true))
        {
            WriteString(objectWriter, "");
            objectWriter.Write(0u);
            WritePlacement(objectWriter);
            WriteChunk(objectWriter, "#RES", []);
            WriteChunk(objectWriter, "MESH", meshData.ToArray());
        }
        var meshObject = CreateChunk("SGPS", sceneObject.ToArray());

        using var root = new MemoryStream();
        using (var rootWriter = new BinaryWriter(root, Encoding.ASCII, leaveOpen: true))
        {
            WriteString(rootWriter, "");
            rootWriter.Write(0u);
            rootWriter.Write(1u);
            rootWriter.Write(meshObject);
            WritePlacement(rootWriter);
            WriteChunk(rootWriter, "NONE", []);
        }
        WriteChunk(writer, "SGNO", root.ToArray());
        return stream.ToArray();
    }

    private static byte[] CreateChunk(string tag, byte[] payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteChunk(writer, tag, payload);
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write(checked((ushort)(bytes.Length + 1)));
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static void WritePlacement(BinaryWriter writer)
    {
        writer.Write(new byte[28]);
        foreach (var value in new[] { 65536, 0, 0, 0, 65536, 0, 0, 0, 65536 })
            writer.Write(value);
        writer.Write(new byte[12]);
    }

    private static byte[] CreateAsphaltBinaryObj(bool invalidIndex = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)2);
        writer.Write(4u);
        foreach (var vertex in new[]
        {
            (X: (short)0, Y: (short)0, Z: (short)0),
            (X: (short)100, Y: (short)0, Z: (short)0),
            (X: (short)0, Y: (short)100, Z: (short)0),
            (X: (short)0, Y: (short)0, Z: (short)100)
        })
        {
            writer.Write(vertex.X);
            writer.Write(vertex.Y);
            writer.Write(vertex.Z);
        }

        writer.Write(new byte[8]);
        writer.Write((byte)1);
        for (var index = 0; index < 4; index++)
        {
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)32767);
        }

        writer.Write(3u);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write(invalidIndex ? (ushort)4 : (ushort)2);
        writer.Write(3u);
        writer.Write((ushort)0);
        writer.Write((ushort)2);
        writer.Write((ushort)3);
        writer.Write(new byte[60]);
        return stream.ToArray();
    }

    private static byte[] CreatePolygon(uint indexType, byte[] indices)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(4u);
        writer.Write(checked((uint)indices.Length));
        writer.Write(0u);
        writer.Write(indexType);
        if (indexType == 0x1401)
            writer.Write(indices);
        else
            foreach (var index in indices)
                writer.Write((ushort)index);
        return stream.ToArray();
    }

    private static byte[] CreateVfs(byte[] block)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("FUFS"));
        writer.Write(0x12345678u);
        writer.Write(1u);
        writer.Write(24u);
        writer.Write(0xAABBCCDDu);
        writer.Write(checked((uint)block.Length));
        writer.Write(block);
        return stream.ToArray();
    }

    private static byte[] CreatePlzp(byte[] payload)
    {
        var compressed = Compress(payload);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("PLZP"));
        writer.Write(checked((uint)payload.Length));
        writer.Write(checked((uint)compressed.Length));
        writer.Write(compressed);
        return stream.ToArray();
    }

    private static byte[] CreateBar()
    {
        var payload = Encoding.ASCII.GetBytes("opaque mesh payload");
        var compressed = Compress(payload);
        using var compressedBlock = new MemoryStream();
        using (var writer = new BinaryWriter(compressedBlock, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(BinaryPrimitives.ReverseEndianness(checked((uint)payload.Length)));
            writer.Write(compressed);
        }

        using var directory = new MemoryStream();
        using (var writer = new BinaryWriter(directory, Encoding.ASCII, leaveOpen: true))
        {
            WriteBarEntry(writer, @"Cars\sample.obj", 0x80, 0);
            WriteBarEntry(writer, @"game\end.end", 0, checked((uint)compressedBlock.Length));
        }

        var region = compressedBlock.ToArray().Concat(new byte[8]).ToArray();
        using var archive = new MemoryStream();
        using (var writer = new BinaryWriter(archive, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(checked((uint)directory.Length));
            writer.Write(checked((uint)region.Length));
            writer.Write(directory.ToArray());
            writer.Write(region);
        }

        return archive.ToArray();
    }

    private static void WriteBarEntry(BinaryWriter writer, string name, byte flags, uint offset)
    {
        writer.Write(Encoding.UTF8.GetBytes(name));
        writer.Write((byte)0);
        writer.Write(flags);
        writer.Write((byte)(offset >> 16));
        writer.Write((byte)(offset >> 8));
        writer.Write((byte)offset);
    }

    private static byte[] CreatePng()
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(stream, "IHDR", header);

        using var imageData = new MemoryStream();
        using (var compressor = new ZLibStream(imageData, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write([0, 255, 0, 0, 255]);
        WritePngChunk(stream, "IDAT", imageData.ToArray());
        WritePngChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WritePngChunk(Stream stream, string name, byte[] payload)
    {
        var type = Encoding.ASCII.GetBytes(name);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)payload.Length));
        stream.Write(length);
        stream.Write(type);
        stream.Write(payload);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ComputeCrc(type, payload));
        stream.Write(crcBytes);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
            crc = UpdateCrc(crc, value);
        foreach (var value in payload)
            crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    private static byte[] CreateJpeg()
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        WriteJpegSegment(stream, 0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]);
        WriteJpegSegment(stream, 0xDA, [1, 1, 0, 0, 63, 0]);
        stream.Write([1, 2, 0xFF, 0, 3, 0xFF, 0xD9]);
        return stream.ToArray();
    }

    private static void WriteJpegSegment(Stream stream, byte marker, byte[] payload)
    {
        stream.Write([0xFF, marker]);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)(payload.Length + 2)));
        stream.Write(length);
        stream.Write(payload);
    }

    private static byte[] Compress(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var compressor = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data);
        return stream.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string tag, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes(tag));
        writer.Write(checked((uint)data.Length));
        writer.Write(data);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
