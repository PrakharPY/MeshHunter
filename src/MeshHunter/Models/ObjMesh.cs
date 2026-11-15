namespace MeshHunter.Models;

public sealed class ObjMesh
{
    public List<(double X, double Y, double Z)> Vertices { get; } = new();
    public List<int> Triangles { get; } = new();
}
