using System.Globalization;
using System.IO;
using MeshHunter.Models;

namespace MeshHunter.Services;

public interface IAssetFormatPlugin
{
    string Name { get; }
    bool CanRead(string path);
    IReadOnlyList<AssetEntry> Analyze(string path);
}

public interface IModelExporter
{
    string Format { get; }
    string FileExtension { get; }
    void Export(ObjMesh mesh, string destinationPath);
}
