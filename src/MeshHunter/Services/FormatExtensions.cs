using MeshHunter.Models;

namespace MeshHunter.Services;

/// <summary>Extension point for adding additional model readers.</summary>
public interface IAssetFormatPlugin
{
    string Name { get; }
    bool CanRead(string path);
    ObjMesh ReadMesh(string path);
}

/// <summary>Common export contract used by model format exporters.</summary>
public interface IModelExporter
{
    string Format { get; }
    string FileExtension { get; }
    void Export(ObjMesh mesh, string destinationPath);
}
