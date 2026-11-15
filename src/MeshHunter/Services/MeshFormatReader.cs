using MeshHunter.Models;

namespace MeshHunter.Services;

public static class MeshFormatReader
{
    public static ObjMesh Read(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".prm":
                return RevoltPrmReader.Read(path);
            case ".i-p":
                return RevoltPrmReader.Read(path, intermediate: true);
            case ".piff":
                return PiffMeshReader.Read(path);
        }

        if (Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase) &&
            AsphaltMeshReader.TryRead(path, out var asphaltMesh))
            return asphaltMesh;
        if (RagingThunderPxcMeshReader.TryRead(path, out var ragingThunderMesh))
            return ragingThunderMesh;
        if (RevoltPrmReader.TryRead(path, out var prmMesh))
            return prmMesh;
        if (PiffMeshReader.TryRead(path, out var piffMesh))
            return piffMesh;
        if (ObjMeshReader.TryRead(path, out var objMesh))
            return objMesh;
        return ObjMeshReader.Read(path);
    }
}
