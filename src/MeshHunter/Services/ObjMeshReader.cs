using System.Globalization;
using MeshHunter.Models;

namespace MeshHunter.Services;

public static class ObjMeshReader
{
    public static ObjMesh Read(string path)
    {
        var mesh = new ObjMesh();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (parts[0] == "v" && parts.Length >= 4)
            {
                mesh.Vertices.Add((Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                continue;
            }

            if (parts[0] == "f")
            {
                var faceIndices = new List<int>();
                foreach (var token in parts.Skip(1))
                {
                    var indexToken = token.Split('/')[0];
                    if (!int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indexValue) || indexValue == 0)
                        throw new InvalidOperationException($"Invalid face index in {Path.GetFileName(path)}: '{token}'.");

                    var finalIndex = indexValue > 0 ? indexValue - 1 : mesh.Vertices.Count + indexValue;
                    if (finalIndex < 0 || finalIndex >= mesh.Vertices.Count)
                        throw new InvalidOperationException($"Face index out of range in {Path.GetFileName(path)}: '{token}'.");

                    faceIndices.Add(finalIndex);
                }

                if (faceIndices.Count >= 3)
                {
                    for (var i = 1; i + 1 < faceIndices.Count; i++)
                    {
                        mesh.Triangles.Add(faceIndices[0]);
                        mesh.Triangles.Add(faceIndices[i]);
                        mesh.Triangles.Add(faceIndices[i + 1]);
                    }
                }
            }
        }

        if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
            throw new InvalidOperationException($"OBJ file '{path}' does not contain any usable triangle data.");

        return mesh;
    }

    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
