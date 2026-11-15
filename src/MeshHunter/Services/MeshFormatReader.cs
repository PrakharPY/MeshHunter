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
        }

        if (RevoltPrmReader.TryRead(path, out var prmMesh))
            return prmMesh;
        if (ObjMeshReader.TryRead(path, out var objMesh))
            return objMesh;
        return ObjMeshReader.Read(path);
    }
}
