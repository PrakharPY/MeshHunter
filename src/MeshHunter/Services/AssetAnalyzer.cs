using System.IO.Compression;
using System.IO;
using MeshHunter.Models;

namespace MeshHunter.Services;

public sealed class AssetAnalyzer
{
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp", ".tif", ".tiff", ".ktx", ".ktx2", ".webp"
    };

    private static readonly HashSet<string> UnsupportedMeshExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".gltf", ".glb", ".stl", ".dae", ".ply", ".p3d"
    };

    public IReadOnlyList<AssetEntry> Analyze(IEnumerable<string> inputs, string workspace, Action<string>? progress = null)
    {
        var results = new List<AssetEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
            GatherFiles(input, results, workspace, visited, 0, progress);
        return results;
    }

    private static void GatherFiles(string path, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth, Action<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath)) return;

        if (Directory.Exists(fullPath))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                GatherFiles(file, results, workspace, visited, depth + 1, progress);
            return;
        }
        if (!File.Exists(fullPath)) return;

        var extension = Path.GetExtension(fullPath);
        var gameExtractRoot = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
        progress?.Invoke($"Analyzing format: {Path.GetFileName(fullPath)}");
        if ((extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) || HasSignature(fullPath, 0x50, 0x4B, 0x03, 0x04)) && depth < 3)
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Archive, "ZIP", "Archive found", "Standard ZIP archive. Its contents are extracted and scanned."));
            var extractRoot = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
            progress?.Invoke($"Extracting ZIP archive: {Path.GetFileName(fullPath)}");
            ExtractZip(fullPath, extractRoot, results, workspace, visited, depth + 1, progress);
        }
        else if ((extension.Equals(".gz", StringComparison.OrdinalIgnoreCase) || HasSignature(fullPath, 0x1F, 0x8B)) && depth < 3)
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Archive, "GZIP", "Compressed file found", "GZIP-compressed content is decompressed and scanned."));
            progress?.Invoke($"Decompressing GZIP file: {Path.GetFileName(fullPath)}");
            var outputPath = DecompressGzip(fullPath, workspace);
            GatherFiles(outputPath, results, workspace, visited, depth + 1, progress);
        }
        else if ((extension.Equals(".zlib", StringComparison.OrdinalIgnoreCase) || HasZlibHeader(fullPath)) && depth < 3)
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Archive, "ZLIB", "Compressed file found", "ZLIB-compressed content is decompressed and scanned."));
            progress?.Invoke($"Decompressing ZLIB file: {Path.GetFileName(fullPath)}");
            var outputPath = DecompressZlib(fullPath, workspace);
            GatherFiles(outputPath, results, workspace, visited, depth + 1, progress);
        }
        else if (extension.Equals(".bar", StringComparison.OrdinalIgnoreCase) && depth < 3 &&
                 GameArchiveExtractor.TryExtractAsphaltBar(fullPath, gameExtractRoot, out var asphaltSummary))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Archive,
                asphaltSummary.Format,
                "Archive found",
                $"Extracted {asphaltSummary.ExtractedCount} of {asphaltSummary.EntryCount} entries; {asphaltSummary.SkippedCount} entries use an unsupported encoding."));
            GatherExtractedFiles(gameExtractRoot, results, workspace, visited, depth, progress);
        }
        else if (depth < 3 && HasSignature(fullPath, 0x46, 0x55, 0x46, 0x53) &&
                 GameArchiveExtractor.TryExtractRagingThunderVfs(fullPath, gameExtractRoot, out var vfsSummary))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Archive,
                vfsSummary.Format,
                "Archive found",
                $"Extracted {vfsSummary.ExtractedCount} of {vfsSummary.EntryCount} entries; discovered meshes and textures are scanned recursively."));
            GatherExtractedFiles(gameExtractRoot, results, workspace, visited, depth, progress);
        }
        else if (extension.Length == 0 && depth < 3 &&
                 EmbeddedImageExtractor.TryExtract(fullPath, gameExtractRoot, out var embeddedImageCount))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Archive,
                "Embedded PNG/JPEG collection",
                "Texture collection found",
                $"Extracted {embeddedImageCount} structurally validated embedded images; other proprietary texture records are not decoded."));
            GatherExtractedFiles(gameExtractRoot, results, workspace, visited, depth, progress);
        }
        else if ((extension.Equals(".piff", StringComparison.OrdinalIgnoreCase) ||
                  HasSignature(fullPath, 0x50, 0x49, 0x46, 0x46)) &&
                 PiffMeshReader.TryRead(fullPath, out _))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Mesh,
                "PIFF",
                "Preview/export supported",
                "PIFF 3D object mesh detected. Select it to preview or export."));
        }
        else if (HasSignature(fullPath, 0x4D, 0x3C, 0x2B, 0x1A) &&
                 RagingThunderPxcMeshReader.TryRead(fullPath, out _))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Mesh,
                "Raging Thunder 2 PXC",
                "Preview/export supported",
                "Polarbit PXC version 19 model geometry detected. Select it to preview or export."));
        }
        else if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            if (AsphaltMeshReader.TryRead(fullPath, out _))
                results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, "Asphalt 4 binary OBJ", "Preview/export supported", "Asphalt 4 binary OBJ mesh detected. Select it to preview or export."));
            else if (ObjMeshReader.TryRead(fullPath, out _))
                results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, "OBJ", "Preview/export supported", "Wavefront OBJ mesh detected. Select it to preview, orbit, and export."));
            else
                results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Other, "OBJ (unparsed)", "Detected; importer not installed", "The file is not a valid Wavefront OBJ mesh supported by this build."));
        }
        else if (extension.Equals(".prm", StringComparison.OrdinalIgnoreCase) || extension.Equals(".i-p", StringComparison.OrdinalIgnoreCase))
        {
            var format = extension.Equals(".prm", StringComparison.OrdinalIgnoreCase) ? "Re-Volt PRM" : "Re-Volt intermediate PRM";
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, format, "Preview/export supported", "Re-Volt polygon mesh detected. Select it to preview or export."));
        }
        else if (TextureExtensions.Contains(extension))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Texture, extension.TrimStart('.').ToUpperInvariant(), "Texture found", "Texture file detected. Texture preview is not included in this milestone."));
        }
        else if (DetectTextureFormat(fullPath) is { } detectedTextureFormat)
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Texture, detectedTextureFormat, "Texture found", "Texture file detected by its binary signature. Texture preview is not included in this milestone."));
        }
        else if (UnsupportedMeshExtensions.Contains(extension))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Other, extension.TrimStart('.').ToUpperInvariant(), "Detected; importer not installed", "This standard model format is recognized but is not yet importable by this build."));
        }
        else if (HasSignature(fullPath, 0x4B, 0x41, 0x50, 0x47))
        {
            results.Add(new(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Other,
                "Fastlane KAPG",
                "Detected; importer not installed",
                "KAPG container signature detected, but its payload encoding is not decoded by this build."));
        }
        else if (RevoltPrmReader.TryRead(fullPath, out _))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, "Re-Volt PRM (detected)", "Preview/export supported", "Re-Volt polygon mesh detected by its binary structure. Select it to preview or export."));
        }
        else if (ObjMeshReader.TryRead(fullPath, out _))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, "OBJ (detected)", "Preview/export supported", "Wavefront OBJ mesh detected by its text structure. Select it to preview or export."));
        }
    }

    private static void GatherExtractedFiles(string extractRoot, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth, Action<string>? progress)
    {
        foreach (var file in Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories))
            GatherFiles(file, results, workspace, visited, depth + 1, progress);
    }

    private static void ExtractZip(string archivePath, string destination, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth, Action<string>? progress)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 20000) throw new InvalidDataException("Archive contains too many entries (limit: 20,000).");
        long totalExpanded = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            totalExpanded += entry.Length;
            if (totalExpanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Archive expands beyond the 2 GiB safety limit.");

            var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe ZIP entry detected: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
            GatherFiles(targetPath, results, workspace, visited, depth, progress);
        }
    }

    private static string DecompressGzip(string compressedPath, string workspace)
    {
        var sourceName = Path.GetFileName(compressedPath);
        var uncompressedName = Path.GetExtension(sourceName).Equals(".gz", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(sourceName)
            : sourceName;
        var outputExtension = Path.GetExtension(uncompressedName);
        var destination = Path.Combine(workspace, Guid.NewGuid().ToString("N") + outputExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var source = File.OpenRead(compressedPath);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var output = File.Create(destination);
        CopyWithExpansionLimit(gzip, output);
        return destination;
    }

    private static string DecompressZlib(string compressedPath, string workspace)
    {
        var sourceName = Path.GetFileName(compressedPath);
        var outputExtension = Path.GetExtension(sourceName).Equals(".zlib", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(Path.GetFileNameWithoutExtension(sourceName))
            : Path.GetExtension(sourceName);
        var destination = Path.Combine(workspace, Guid.NewGuid().ToString("N") + outputExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var source = File.OpenRead(compressedPath);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var output = File.Create(destination);
        CopyWithExpansionLimit(zlib, output);
        return destination;
    }

    private static void CopyWithExpansionLimit(Stream source, Stream destination)
    {
        var buffer = new byte[81920];
        long expanded = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            expanded += read;
            if (expanded > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Compressed file expands beyond the 2 GiB safety limit.");
            destination.Write(buffer, 0, read);
        }
    }

    private static bool HasSignature(string path, params byte[] signature)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < signature.Length) return false;
        Span<byte> actual = stackalloc byte[signature.Length];
        return ReadPrefix(stream, actual) == actual.Length && actual.SequenceEqual(signature);
    }

    private static bool HasZlibHeader(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[2];
        if (ReadPrefix(stream, header) != header.Length) return false;
        var compressionMethod = header[0] & 0x0F;
        var windowSize = header[0] >> 4;
        var headerValue = (header[0] << 8) | header[1];
        return compressionMethod == 8 && windowSize <= 7 && headerValue % 31 == 0;
    }

    private static string? DetectTextureFormat(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        var read = ReadPrefix(stream, header);
        var bytes = header[..read];

        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG";
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "JPEG";
        if (bytes.StartsWith("DDS "u8)) return "DDS";
        if (bytes.StartsWith("BM"u8)) return "BMP";
        if (bytes.StartsWith("II*\0"u8) || bytes.StartsWith("MM\0*"u8)) return "TIFF";
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8)) return "WEBP";
        if (bytes.StartsWith(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB })) return "KTX2";
        if (bytes.StartsWith(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB })) return "KTX";
        return null;
    }

    private static int ReadPrefix(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
