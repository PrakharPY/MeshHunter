using System.Globalization;
using System.Text;
using MeshHunter.Models;

namespace MeshHunter.Services;

public sealed class ObjExporter : IModelExporter
{
    public string Format => "Wavefront OBJ";
    public string FileExtension => ".obj";

    public void Export(ObjMesh mesh, string destinationPath)
    {
        using var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(false));
        writer.WriteLine("# Exported by MeshHunter");
        foreach (var (x, y, z) in mesh.Vertices)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"v {x:R} {y:R} {z:R}"));
        for (var i = 0; i + 2 < mesh.Triangles.Count; i += 3)
            writer.WriteLine($"f {mesh.Triangles[i] + 1} {mesh.Triangles[i + 1] + 1} {mesh.Triangles[i + 2] + 1}");
    }
}
