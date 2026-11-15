using System.IO.Compression;
using MeshHunter.Models;

namespace MeshHunter.Services;

public sealed class AssetAnalyzer
{
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp", ".tif", ".tiff", ".ktx", ".webp"
    };

    private static readonly HashSet<string> GamePackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".assets", ".bundle", ".uasset", ".umap", ".pak", ".utoc", ".ucas"
    };

    public IReadOnlyList<AssetEntry> Analyze(IEnumerable<string> inputs, string workspace)
    {
        var results = new List<AssetEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            GatherFiles(input, results, workspace, visited, 0);
        }

        return results;
    }

    private static void GatherFiles(string path, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath))
            return;

        if (Directory.Exists(fullPath))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                GatherFiles(file, results, workspace, visited, depth + 1);
            }
            return;
        }

        if (!File.Exists(fullPath))
            return;

        var extension = Path.GetExtension(fullPath);

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) && depth < 3)
        {
            var archiveEntry = new AssetEntry(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Archive,
                "ZIP",
                "Archive found",
                "ZIP archive discovered. Contents are extracted and scanned for assets.");
            results.Add(archiveEntry);

            var extractRoot = Path.Combine(workspace, Guid.NewGuid().ToString("N"));
            ExtractZip(fullPath, extractRoot, results, workspace, visited, depth + 1);
            return;
        }

        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new AssetEntry(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Mesh,
                "OBJ",
                "Preview/export supported",
                "Wavefront OBJ mesh detected. Select this item to preview it in the viewport."));
            return;
        }

        if (TextureExtensions.Contains(extension))
        {
            results.Add(new AssetEntry(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Texture,
                extension.TrimStart('.').ToUpperInvariant(),
                "Texture found",
                "Texture file detected. Advanced texture preview can be added in later revisions."));
            return;
        }

        if (GamePackageExtensions.Contains(extension))
        {
            results.Add(new AssetEntry(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.GamePackage,
                extension.TrimStart('.').ToUpperInvariant(),
                "Recognized game-package extension",
                "This looks like a proprietary game asset package. Engine-specific parsing is not implemented in this MVP."));
            return;
        }

        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".stl", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new AssetEntry(
                Path.GetFileName(fullPath),
                fullPath,
                AssetKind.Other,
                extension.TrimStart('.').ToUpperInvariant(),
                "Detected; importer not installed",
                "This file is recognized as a mesh format but not yet imported by the current starter."));
        }
    }

    private static void ExtractZip(string archivePath, string destination, List<AssetEntry> results, string workspace, HashSet<string> visited, int depth)
    {
        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsafe ZIP entry detected: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);

            if (entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && depth < 3)
            {
                GatherFiles(targetPath, results, workspace, visited, depth + 1);
            }
            else
            {
                GatherFiles(targetPath, results, workspace, visited, depth + 1);
            }
        }
    }
}
