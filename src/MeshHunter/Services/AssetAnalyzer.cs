using System.IO.Compression;
using MeshHunter.Models;

namespace MeshHunter.Services;

public sealed class AssetAnalyzer
{
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp", ".tif", ".tiff", ".ktx", ".webp"
    };

    private static readonly HashSet<string> UnsupportedMeshExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".gltf", ".glb", ".stl", ".dae", ".ply"
    };

    public IReadOnlyList<AssetEntry> Analyze(IEnumerable<string> inputs, string workspace)
    {
        var results = new List<AssetEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
            GatherFiles(input, results, workspace, visited, 0);
        return results;
    }

    private static void GatherFiles(string path, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath)) return;

        if (Directory.Exists(fullPath))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                GatherFiles(file, results, workspace, visited, depth + 1);
            return;
        }
        if (!File.Exists(fullPath)) return;

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) && depth < 3)
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Archive, "ZIP", "Archive found", "Standard ZIP archive. Its contents are extracted and scanned."));
            var extractRoot = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
            ExtractZip(fullPath, extractRoot, results, workspace, visited, depth + 1);
        }
        else if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Mesh, "OBJ", "Preview/export supported", "Wavefront OBJ mesh detected. Select it to preview, orbit, and export."));
        }
        else if (TextureExtensions.Contains(extension))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Texture, extension.TrimStart('.').ToUpperInvariant(), "Texture found", "Texture file detected. Texture preview is not included in this milestone."));
        }
        else if (UnsupportedMeshExtensions.Contains(extension))
        {
            results.Add(new(Path.GetFileName(fullPath), fullPath, AssetKind.Other, extension.TrimStart('.').ToUpperInvariant(), "Detected; importer not installed", "This standard model format is recognized but is not yet importable by this build."));
        }
    }

    private static void ExtractZip(string archivePath, string destination, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth)
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
            GatherFiles(targetPath, results, workspace, visited, depth);
        }
    }
}
